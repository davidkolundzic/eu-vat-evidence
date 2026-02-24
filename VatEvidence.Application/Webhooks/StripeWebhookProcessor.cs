using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Stripe;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Application.Options;
using VatEvidence.Domain;

namespace VatEvidence.Application.Webhooks;

public sealed partial class StripeWebhookProcessor(
  IAppDbContext _db,
  ILogger<StripeWebhookProcessor> _logger,
  IEvidenceAppendService _evidenceAppendService,
  IOptions<StripeOptions> _stripeOptions) : IWebhookProcessor
{
  public async Task<WebhookProcessResult> ProcessAsync(ProcessWebhookCommand cmd, CancellationToken ct = default)
  {
    // 1) Save to provider_events (idempotent) OR load existing event if duplicate
    var providerEvent = await SaveOrLoadEventAsync(cmd, ct);

    // Ako je ve? Processed -> pravi duplicate, možemo odmah završiti
    if (providerEvent.ProcessingStatus == EventProcessingStatus.Processed)
    {
      LogDuplicateEvent(cmd.EventId);
      return new WebhookProcessResult(true, providerEvent.Id, "Duplicate event", false);
    }

    // 2) Extract payment_intent ID from payload (universal across event types)
    var piId = StripePayloadExtractor.ExtractPaymentIntentId(cmd.EventType, cmd.PayloadJson);

    if (string.IsNullOrWhiteSpace(piId))
    {
      LogUnhandledEventType(cmd.EventType);

      // Mark as processed (non-retryable: event doesn't contain piId)
      try
      {
        await _db.ProviderEvents
          .Where(x => x.Id == providerEvent.Id)
          .ExecuteUpdateAsync(s => s
            .SetProperty(p => p.ProcessingStatus, EventProcessingStatus.Processed)
            .SetProperty(p => p.Error, (string?)null),
            ct);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to update provider_event status for unhandled event {EventId}", cmd.EventId);
        if (IsRetryable(ex)) throw;
      }

      return new WebhookProcessResult(true, providerEvent.Id, "Event without payment_intent (non-retryable)", false);
    }

    // 3) Process using canonical Stripe API fetch
    try
    {
      await ProcessStripeTransactionAsync(
        cmd.WorkspaceId,
        cmd.Mode,
        piId,
        cmd.IpCountryHint,
        providerEvent.ReceivedUtc,
        ct);

      // Mark as processed atomi?no sa ExecuteUpdateAsync (bez tracking dependency)
      try
      {
        await _db.ProviderEvents
          .Where(x => x.Id == providerEvent.Id)
          .ExecuteUpdateAsync(s => s
            .SetProperty(p => p.ProcessingStatus, EventProcessingStatus.Processed)
            .SetProperty(p => p.Error, (string?)null),
            ct);
      }
      catch (Exception ex)
      {
        // Log warning if status update fails, but treat webhook processing as successful
        // (event was processed, only status update failed - rare edge case)
        _logger.LogWarning(ex, "Failed to update provider_event status to Processed for event {EventId}", cmd.EventId);

        // If failure is retryable (DB timeout, deadlock), re-throw so Stripe retries
        if (IsRetryable(ex))
          throw;
      }

      return new WebhookProcessResult(true, providerEvent.Id, null, false);
    }
    catch (Exception ex)
    {
      LogProcessingError(ex, cmd.EventId);

      // Mark as failed atomi?no sa ExecuteUpdateAsync
      try
      {
        await _db.ProviderEvents
          .Where(x => x.Id == providerEvent.Id)
          .ExecuteUpdateAsync(s => s
            .SetProperty(p => p.ProcessingStatus, EventProcessingStatus.Failed)
            .SetProperty(p => p.Error, ex.Message),
            ct);
      }
      catch (Exception ex2)
      {
        // Log warning if status update fails, but preserve original exception
        // (secondary failure during status update should not mask the primary failure)
        _logger.LogWarning(ex2, "Failed to update provider_event status to Failed for event {EventId}", cmd.EventId);

        // If status update failure is retryable, re-throw ORIGINAL exception (not ex2)
        // so Stripe retries and we can process again
        if (IsRetryable(ex2))
          throw;
      }

      var retryable = IsRetryable(ex);
      return new WebhookProcessResult(false, providerEvent.Id, ex.Message, retryable);
    }
  }

  private async Task<ProviderEvent> SaveOrLoadEventAsync(ProcessWebhookCommand cmd, CancellationToken ct)
  {
    var providerKind = string.Equals(cmd.Provider, "stripe", StringComparison.OrdinalIgnoreCase)
      ? ProviderKind.Stripe 
      : throw new ArgumentException($"Unknown provider: {cmd.Provider}");

    var mode = string.Equals(cmd.Mode, "test", StringComparison.OrdinalIgnoreCase)
      ? ProviderMode.Test 
      : ProviderMode.Live;

    var payloadHash = ComputeSha256(cmd.PayloadJson);

    var providerEvent = new ProviderEvent
    {
      Id = Guid.NewGuid(),
      WorkspaceId = cmd.WorkspaceId,
      Provider = providerKind,
      Mode = mode,
      ProviderEventId = cmd.EventId,
      Type = cmd.EventType,
      CreatedUtc = cmd.CreatedUtc,
      ReceivedUtc = DateTimeOffset.UtcNow,
      PayloadJson = JsonDocument.Parse(cmd.PayloadJson),
      PayloadHash = payloadHash,
      ProcessingStatus = EventProcessingStatus.Received,
      Error = null
    };

    try
    {
      _db.ProviderEvents.Add(providerEvent);
      await _db.SaveChangesAsync(ct);
      return providerEvent;
    }
    catch (DbUpdateException ex)
      when (ex.InnerException is PostgresException pex
            && pex.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(
                pex.ConstraintName,
                "ix_provider_events_workspace_id_provider_mode_provider_event_id",
                StringComparison.Ordinal))
    {
      // Duplicate event: u?itaj postoje?i iz DB i vrati ga (bez tracking-a)
      var existing = await _db.ProviderEvents
        .AsNoTracking()
        .SingleAsync(x =>
          x.WorkspaceId == cmd.WorkspaceId &&
          x.Provider == providerKind &&
          x.Mode == mode &&
          x.ProviderEventId == cmd.EventId, ct);

      return existing;
    }
  }

  /// <summary>
  /// Canonical Stripe transaction processing using server-to-server API fetch.
  /// Single pipeline: Fetch canonical state -> Upsert Transaction -> Append Evidence -> Evaluate -> Commit.
  /// </summary>
  private async Task ProcessStripeTransactionAsync(
    Guid workspaceId,
    string mode,
    string piId,
    string? ipCountryHint,
    DateTimeOffset receivedUtc,
    CancellationToken ct)
  {
    // 1) Select Stripe API key based on mode
    var apiKey = string.Equals(mode, "test", StringComparison.OrdinalIgnoreCase)
      ? _stripeOptions.Value.TestSecretKey
      : _stripeOptions.Value.LiveSecretKey;

    if (string.IsNullOrWhiteSpace(apiKey))
    {
      throw new InvalidOperationException($"Stripe API key not configured for mode: {mode}");
    }

    // 2) Fetch canonical PaymentIntent state from Stripe API
    var requestOptions = new RequestOptions { ApiKey = apiKey };
    var piService = new PaymentIntentService();

    PaymentIntent paymentIntent;
    try
    {
      paymentIntent = await piService.GetAsync(piId, new PaymentIntentGetOptions
      {
        Expand = ["latest_charge"]
      }, requestOptions, ct);
    }
    catch (StripeException ex)
    {
      // 404 = PaymentIntent doesn't exist (non-retryable)
      // Stripe.NET throws StripeException with HttpStatusCode = 404 instead of returning null
      if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
          ex.StripeError?.Code == "resource_missing")
      {
        _logger.LogWarning(ex, "PaymentIntent {PaymentIntentId} not found in Stripe (mode={Mode}). Non-retryable, skipping.", piId, mode);
        return; // Don't throw - let caller mark event as processed
      }

      // Other errors (timeout, rate limit, 5xx) are retryable
      _logger.LogError(ex, "Failed to fetch PaymentIntent {PaymentIntentId} from Stripe API (mode={Mode}). Retryable.", piId, mode);
      throw; // Retryable: Stripe will retry
    }

    // 3) Extract canonical billing country from latest_charge.billing_details
    string? billingCountry = null;
    string? chargeId = null;

    if (paymentIntent.LatestCharge is Charge charge)
    {
      chargeId = charge.Id;

      if (charge.BillingDetails?.Address?.Country is { } country &&
          IsValidCountryCode(country))
      {
        billingCountry = country.Trim().ToUpperInvariant();
      }
    }

    // 4) Extract IP country from hint (Stripe doesn't expose IP directly)
    string? ipCountry = null;
    if (!string.IsNullOrWhiteSpace(ipCountryHint))
    {
      var hintCode = ipCountryHint.Trim().ToUpperInvariant();
      if (IsValidCountryCode(hintCode))
      {
        ipCountry = hintCode;
      }
    }

    // 5) Extract customer email (fallback chain: charge.billing_details.email -> pi.receipt_email)
    string? customerEmail = null;
    if (paymentIntent.LatestCharge is Charge chg && !string.IsNullOrWhiteSpace(chg.BillingDetails?.Email))
    {
      customerEmail = chg.BillingDetails.Email;
    }
    else if (!string.IsNullOrWhiteSpace(paymentIntent.ReceiptEmail))
    {
      customerEmail = paymentIntent.ReceiptEmail;
    }

    var createdUtc = new DateTimeOffset(DateTime.SpecifyKind(paymentIntent.Created, DateTimeKind.Utc));

    LogCanonicalFetch(piId, billingCountry, ipCountry);

    // 6) Wrap entire flow in DB transaction
    await using var dbTx = await _db.BeginTransactionAsync(ct);

    var txMode = string.Equals(mode, "test", StringComparison.OrdinalIgnoreCase)
      ? ProviderMode.Test
      : ProviderMode.Live;

    // 7) Upsert Transaction
    var transaction = await _db.Transactions
      .SingleOrDefaultAsync(x =>
        x.WorkspaceId == workspaceId &&
        x.Provider == ProviderKind.Stripe &&
        x.Mode == txMode &&
        x.ProviderTransactionId == piId, ct);

    if (transaction is null)
    {
      transaction = new Transaction
      {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        Provider = ProviderKind.Stripe,
        Mode = txMode,
        ProviderTransactionId = piId,
        ProviderChargeId = chargeId,
        AmountMinor = paymentIntent.Amount,
        Currency = paymentIntent.Currency?.ToUpperInvariant() ?? "EUR",
        CustomerEmail = customerEmail,
        CreatedUtc = createdUtc,
        Status = TransactionStatus.Insufficient,
        StatusReason = "Awaiting evidence"
      };

      _db.Transactions.Add(transaction);

      try
      {
        await _db.SaveChangesAsync(ct);
        LogTransactionCreated(transaction.Id, piId);
      }
      catch (DbUpdateException ex) when (IsTransactionUniqueViolation(ex))
      {
        // Parallel webhook created same transaction - reload and refresh
        transaction = await _db.Transactions.SingleAsync(x =>
          x.WorkspaceId == workspaceId &&
          x.Provider == ProviderKind.Stripe &&
          x.Mode == txMode &&
          x.ProviderTransactionId == piId, ct);

        if (transaction.ProviderChargeId is null && chargeId is not null)
          transaction.ProviderChargeId = chargeId;
        if (transaction.CustomerEmail is null && customerEmail is not null)
          transaction.CustomerEmail = customerEmail;
      }
    }
    else
    {
      // Refresh missing fields
      if (transaction.ProviderChargeId is null && chargeId is not null)
        transaction.ProviderChargeId = chargeId;
      if (transaction.CustomerEmail is null && customerEmail is not null)
        transaction.CustomerEmail = customerEmail;
    }

    // 8) Append billing evidence (if available)
    if (!string.IsNullOrWhiteSpace(billingCountry) && !string.IsNullOrWhiteSpace(chargeId))
    {
      var billingSnapshot = StripePayloadExtractor.CreateBillingSnapshot(
        chargeId,
        billingCountry,
        paymentIntent.LatestCharge?.BillingDetails?.Address);

      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Billingcountry,
          CountryCode: billingCountry,
          SourceRef: $"stripe:charge:{chargeId}:billing",
          ValueRaw: billingSnapshot,
          CapturedUtc: receivedUtc
        ),
        ct);

      try
      {
        await _db.SaveChangesAsync(ct);
        LogBillingCountryAppended(billingCountry, transaction.Id);
      }
      catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
      {
        _logger.LogInformation("Billing evidence already exists for Transaction={TransactionId} (parallel webhook)", transaction.Id);
      }
    }

    // 9) Append IP evidence (if available)
    if (!string.IsNullOrWhiteSpace(ipCountry))
    {
      // ipCountry is non-null only if ipCountryHint was present and valid
      var ipSnapshot = StripePayloadExtractor.CreateIpSnapshot(
        ipCountry,
        "CF-IPCountry",
        headerPresent: !string.IsNullOrWhiteSpace(ipCountryHint));

      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Ipcountry,
          CountryCode: ipCountry,
          SourceRef: $"cf-ipcountry",
          ValueRaw: ipSnapshot,
          CapturedUtc: receivedUtc
        ),
        ct);

      try
      {
        await _db.SaveChangesAsync(ct);
        LogIpCountryAppended(ipCountry, transaction.Id);
      }
      catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
      {
        _logger.LogInformation("IP evidence already exists for Transaction={TransactionId} (parallel webhook)", transaction.Id);
      }
    }

    // 10) Evaluate status from current evidence snapshot
    var (status, reason) = await EvaluateStatusAsync(transaction.Id, ct);
    transaction.Status = status;
    transaction.StatusReason = reason;

    await _db.SaveChangesAsync(ct);
    await dbTx.CommitAsync(ct);
  }

  private async Task<(TransactionStatus Status, string Reason)> EvaluateStatusAsync(Guid transactionId, CancellationToken ct)
  {
    // Latest per type by sequence
    var latest = await _db.EvidenceRecords
      .AsNoTracking()
      .Where(x => x.TransactionId == transactionId &&
                  (x.EvidenceType == EvidenceType.Billingcountry || x.EvidenceType == EvidenceType.Ipcountry))
      .GroupBy(x => x.EvidenceType)
      .Select(g => g.OrderByDescending(x => x.Sequence).First())
      .ToListAsync(ct);

    var billingRaw = latest.SingleOrDefault(x => x.EvidenceType == EvidenceType.Billingcountry)?.CountryCode;
    var ipRaw = latest.SingleOrDefault(x => x.EvidenceType == EvidenceType.Ipcountry)?.CountryCode;

    var billingCtx = CountryClassification.Classify(billingRaw);
    var ipCtx = CountryClassification.Classify(ipRaw);


    if (!billingCtx.IsValid || !ipCtx.IsValid)
    {
      return (TransactionStatus.Insufficient, "Missing required evidence (billing/ip)");
    }

    if (!string.Equals(billingCtx.Code, ipCtx.Code, StringComparison.Ordinal))
    {
      return (TransactionStatus.Mismatch, $"Evidence mismatch (billing={billingCtx.Code}, ip={ipCtx.Code})");
    }

    // (optional) dodatna info u reason:
     var region = billingCtx.IsEu ? "EU" : "non-EU";

     return (TransactionStatus.Ok, $"Evidence OK {billingCtx.Code}  (billing matches IP) [{region}]");

  }

  private static string ComputeSha256(string input)
  {
    var bytes = Encoding.UTF8.GetBytes(input);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  private static bool IsRetryable(Exception ex)
  {
    // Transient infra/DB issues -> Stripe should retry
    if (ex is TimeoutException) return true;
    if (ex is DbException) return true;
    if (ex is DbUpdateException dbu && dbu.InnerException is DbException) return true;

    // Postgres transient classes: deadlock/serialization/timeouts
    if (ex is DbUpdateException { InnerException: PostgresException pex })
    {
      return pex.SqlState is PostgresErrorCodes.DeadlockDetected
        or PostgresErrorCodes.SerializationFailure
        or PostgresErrorCodes.ConnectionException
        or PostgresErrorCodes.ConnectionDoesNotExist
        or PostgresErrorCodes.ConnectionFailure;
    }

    return false;
  }

  private static bool IsDuplicateKeyViolation(DbUpdateException ex)
  {
    return ex.InnerException is PostgresException pex
           && pex.SqlState == PostgresErrorCodes.UniqueViolation
           && (pex.ConstraintName == "ux_evidence_records_tx_type_source"
               || pex.ConstraintName == "ux_evidence_records_tx_sequence");
  }

  private static bool IsTransactionUniqueViolation(DbUpdateException ex)
  {
    return ex.InnerException is PostgresException pex
           && pex.SqlState == PostgresErrorCodes.UniqueViolation
           && pex.ConstraintName == "ix_transactions_workspace_id_provider_mode_provider_transaction_id";
  }

  private static bool IsValidCountryCode(string? code)
  {
    // Simple validation: must be 2-letter uppercase code
    return code is { Length: 2 } && char.IsLetter(code[0]) && char.IsLetter(code[1]);
  }
}
