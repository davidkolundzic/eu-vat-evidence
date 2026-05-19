using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Application.Stripe;
using VatEvidence.Domain;

namespace VatEvidence.Application.Webhooks;

public sealed partial class StripeWebhookProcessor(
  IAppDbContext _db,
  ILogger<StripeWebhookProcessor> _logger,
  IEvidenceAppendService _evidenceAppendService,
  IStripeCanonicalReader _canonicalReader) : IWebhookProcessor
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
      // KLJU?NO: detach failed insert iz ChangeTracker-a da se ne pokuša ponovo insertovati
      // kasnije u ProcessStripeTransactionAsync kada se pozove SaveChangesAsync
      // Note: Cast to DbContext needed because Entry is not exposed via IAppDbContext interface
      if (_db is DbContext dbContext)
      {
        dbContext.Entry(providerEvent).State = EntityState.Detached;
      }

      // Duplicate event: u?itaj postoje?i iz DB i vrati ga (bez tracking-a)
      var existing = await _db.ProviderEvents
        .AsNoTracking()
        .SingleAsync(x =>
          x.WorkspaceId == cmd.WorkspaceId &&
          x.Provider == providerKind &&
          x.Mode == mode &&
          x.ProviderEventId == cmd.EventId, ct);

      _logger.LogInformation("Duplicate provider_event detected: EventId={EventId}, WorkspaceId={WorkspaceId}, Mode={Mode}. Loaded existing from DB.",
        cmd.EventId, cmd.WorkspaceId, mode);

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
    // 1) Fetch canonical PaymentIntent state from Stripe API via dedicated reader service
    var stripeMode = string.Equals(mode, "test", StringComparison.OrdinalIgnoreCase)
      ? StripeMode.Test
      : StripeMode.Live;

    CanonicalStripeSnapshot snapshot;
    try
    {
      snapshot = await _canonicalReader.ReadAsync(piId, stripeMode, ct);
    }
    catch (global::Stripe.StripeException ex)
    {
      // 404 = PaymentIntent doesn't exist (non-retryable)
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

    // 2) Extract billing country from snapshot
    string? billingCountry = snapshot.BillingCountry;

    LogCanonicalFetch(piId, billingCountry, null);

    // 3) Wrap entire flow in DB transaction
    await using var dbTx = await _db.BeginTransactionAsync(ct);

    var txMode = string.Equals(mode, "test", StringComparison.OrdinalIgnoreCase)
      ? ProviderMode.Test
      : ProviderMode.Live;

    // 4) Upsert Transaction
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
        ProviderChargeId = snapshot.ChargeId,
        AmountMinor = snapshot.AmountMinor,
        Currency = snapshot.Currency,
        CustomerEmail = snapshot.CustomerEmail,
        CreatedUtc = snapshot.CreatedUtc,
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

        if (transaction.ProviderChargeId is null && snapshot.ChargeId is not null)
          transaction.ProviderChargeId = snapshot.ChargeId;
        if (transaction.CustomerEmail is null && snapshot.CustomerEmail is not null)
          transaction.CustomerEmail = snapshot.CustomerEmail;
      }
    }
    else
    {
      // Refresh missing fields
      if (transaction.ProviderChargeId is null && snapshot.ChargeId is not null)
        transaction.ProviderChargeId = snapshot.ChargeId;
      if (transaction.CustomerEmail is null && snapshot.CustomerEmail is not null)
        transaction.CustomerEmail = snapshot.CustomerEmail;
    }

    // 5) Append billing evidence (if available)
    if (!string.IsNullOrWhiteSpace(billingCountry) && !string.IsNullOrWhiteSpace(snapshot.ChargeId))
    {
      var billingSnapshot = StripePayloadExtractor.CreateBillingSnapshot(
        snapshot.ChargeId,
        billingCountry,
        snapshot.BillingAddress);

      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Billingcountry,
          CountryCode: billingCountry,
          SourceRef: $"stripe:charge:{snapshot.ChargeId}:billing",
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

    // 6) Evaluate status from current evidence snapshot
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
