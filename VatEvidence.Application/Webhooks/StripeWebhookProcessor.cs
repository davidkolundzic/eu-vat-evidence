using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Domain;

namespace VatEvidence.Application.Webhooks;

public sealed partial class StripeWebhookProcessor(
  IAppDbContext _db,
  ILogger<StripeWebhookProcessor> _logger,
  IEvidenceAppendService _evidenceAppendService) : IWebhookProcessor
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

    // 2) Process event based on type (MVP: only payment_intent.succeeded)
    try
    {
      if (cmd.EventType == "payment_intent.succeeded")
      {
        await ProcessPaymentIntentSucceededAsync(
          cmd.WorkspaceId,
          cmd.Mode,
          providerEvent.ProviderEventId,
          providerEvent.ReceivedUtc,
          cmd.PayloadJson,
          cmd.IpCountryHint,
          ct);
      }
      else if (cmd.EventType == "checkout.session.completed")
      {
        await ProcessCheckoutSessionCompletedAsync(
          cmd.WorkspaceId,
          cmd.Mode,
          providerEvent.ProviderEventId,
          providerEvent.ReceivedUtc,
          cmd.PayloadJson,
          cmd.IpCountryHint,
          ct);
      }
      else
      {
        LogUnhandledEventType(cmd.EventType);
      }

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

  private async Task ProcessCheckoutSessionCompletedAsync(
    Guid workspaceId, 
    string mode, 
    string providerEventId, 
    DateTimeOffset receivedUtc, 
    string payloadJson,
    string? ipCountryHint,
    CancellationToken ct)
  {
    LogRawWebhookPayload("checkout.session.completed", payloadJson);

    var doc = JsonDocument.Parse(payloadJson);
    var dataObj = doc.RootElement.GetProperty("data").GetProperty("object");

    // Extract session ID
    var sessionId = dataObj.GetProperty("id").GetString() ?? "";

    // Extract payment_intent ID (required)
    string? piId = null;
    if (dataObj.TryGetProperty("payment_intent", out var piElement) && piElement.ValueKind == JsonValueKind.String)
    {
      piId = piElement.GetString();
    }

    if (string.IsNullOrWhiteSpace(piId))
    {
      LogCheckoutSessionMissingPaymentIntent(sessionId);
      return; // Non-retryable: session without payment_intent (e.g., setup mode)
    }

    // Extract billing country from customer_details.address.country
    string? billingCountry = null;
    if (dataObj.TryGetProperty("customer_details", out var customerDetails) &&
        customerDetails.ValueKind == JsonValueKind.Object &&
        customerDetails.TryGetProperty("address", out var address) &&
        address.ValueKind == JsonValueKind.Object &&
        address.TryGetProperty("country", out var country) &&
        country.ValueKind == JsonValueKind.String)
    {
      var countryCode = country.GetString()?.Trim().ToUpperInvariant();
      if (IsValidCountryCode(countryCode))
      {
        billingCountry = countryCode;
      }
    }

    if (string.IsNullOrWhiteSpace(billingCountry))
    {
      LogCheckoutSessionMissingBillingCountry(sessionId, piId);
    }

    // Extract amount_total and currency from session
    var amountTotal = dataObj.TryGetProperty("amount_total", out var amtElement) && amtElement.ValueKind == JsonValueKind.Number
      ? amtElement.GetInt64()
      : 0;

    var currency = dataObj.TryGetProperty("currency", out var curElement) && curElement.ValueKind == JsonValueKind.String
      ? (curElement.GetString()?.Trim().ToUpperInvariant() ?? "EUR")
      : "EUR";

    // Extract customer email from customer_details
    string? customerEmail = null;
    if (dataObj.TryGetProperty("customer_details", out var custDetails) &&
        custDetails.ValueKind == JsonValueKind.Object &&
        custDetails.TryGetProperty("email", out var emailElement) &&
        emailElement.ValueKind == JsonValueKind.String)
    {
      customerEmail = emailElement.GetString();
    }

    // Extract session created timestamp
    var createdUtc = dataObj.TryGetProperty("created", out var createdElement) && createdElement.ValueKind == JsonValueKind.Number
      ? DateTimeOffset.FromUnixTimeSeconds(createdElement.GetInt64())
      : DateTimeOffset.UtcNow;

    LogCheckoutSessionReceived(sessionId, piId, billingCountry);

    // Wrap entire flow in transaction for FOR UPDATE lock
    await using var dbTx = await _db.BeginTransactionAsync(ct);

    var txMode = string.Equals(mode, "test", StringComparison.OrdinalIgnoreCase) ? ProviderMode.Test : ProviderMode.Live;

    // 1) Find or create transaction by payment_intent ID
    var transaction = await _db.Transactions
      .SingleOrDefaultAsync(x =>
        x.WorkspaceId == workspaceId &&
        x.Provider == ProviderKind.Stripe &&
        x.Mode == txMode &&
        x.ProviderTransactionId == piId, ct);

    if (transaction is null)
    {
      // Create transaction with real data from checkout session
      transaction = new Transaction
      {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        Provider = ProviderKind.Stripe,
        Mode = txMode,
        ProviderTransactionId = piId,
        ProviderChargeId = null,
        AmountMinor = amountTotal,
        Currency = currency,
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
        // Parallel webhook created same transaction - load it and refresh missing fields
        transaction = await _db.Transactions.SingleAsync(x =>
          x.WorkspaceId == workspaceId &&
          x.Provider == ProviderKind.Stripe &&
          x.Mode == txMode &&
          x.ProviderTransactionId == piId, ct);

        // Refresh missing fields (same logic as else branch)
        if (transaction.AmountMinor == 0 && amountTotal > 0) transaction.AmountMinor = amountTotal;
        if (transaction.Currency == "EUR" && currency != "EUR") transaction.Currency = currency;
        if (transaction.CustomerEmail is null && customerEmail is not null) transaction.CustomerEmail = customerEmail;
      }
    }
    else
    {
      // Update transaction with session data if missing (payment_intent.succeeded may not have fired yet)
      if (transaction.AmountMinor == 0 && amountTotal > 0) transaction.AmountMinor = amountTotal;
      if (transaction.Currency == "EUR" && currency != "EUR") transaction.Currency = currency;
      if (transaction.CustomerEmail is null && customerEmail is not null) transaction.CustomerEmail = customerEmail;
    }

    // 2) Append billing evidence (only if available)
    if (!string.IsNullOrWhiteSpace(billingCountry))
    {
      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Billingcountry,
          CountryCode: billingCountry,
          SourceRef: $"{piId}:billing",
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
        // Parallel webhook appended same evidence - this is OK (idempotent)
        _logger.LogInformation("Billing evidence already exists for Transaction={TransactionId}, SourceRef={SourceRef} (parallel webhook)", transaction.Id, $"{piId}:billing");
      }
    }

    // 3) Evaluate status from current evidence snapshot
    var (status, reason) = await EvaluateStatusAsync(transaction.Id, ct);
    transaction.Status = status;
    transaction.StatusReason = reason;

    await _db.SaveChangesAsync(ct);
    await dbTx.CommitAsync(ct);
  }

  private async Task ProcessPaymentIntentSucceededAsync(
    Guid workspaceId, 
    string mode, 
    string providerEventId, 
    DateTimeOffset receivedUtc, 
    string payloadJson,
    string? ipCountryHint,
    CancellationToken ct)
  {
    var doc = JsonDocument.Parse(payloadJson);
    var dataObj = doc.RootElement.GetProperty("data").GetProperty("object");

    // Extract payment intent details
    var piId = dataObj.GetProperty("id").GetString() ?? "";
    var amount = dataObj.GetProperty("amount").GetInt64();
    var currency = dataObj.GetProperty("currency").GetString()?.ToUpperInvariant() ?? "EUR";
    var created = dataObj.GetProperty("created").GetInt64();
    var createdUtc = DateTimeOffset.FromUnixTimeSeconds(created);

    // Extract charge ID (if available)
    string? chargeId = null;
    if (dataObj.TryGetProperty("latest_charge", out var chargeIdElement) && chargeIdElement.ValueKind == JsonValueKind.String)
    {
      chargeId = chargeIdElement.GetString();
    }

    // Extract customer email (if available)
    string? customerEmail = null;
    if (dataObj.TryGetProperty("receipt_email", out var emailElement) && emailElement.ValueKind == JsonValueKind.String)
    {
      customerEmail = emailElement.GetString();
    }

    // Extract billing country from billing_details
    var billingCountry = ExtractBillingCountry(dataObj);
    if (string.IsNullOrWhiteSpace(billingCountry))
    {
      LogNoBillingCountry(piId);
    }

    // Extract IP country from hint, metadata, or charges
    var ipCountry = ExtractIpCountry(dataObj, payloadJson, ipCountryHint);
    if (string.IsNullOrWhiteSpace(ipCountry))
    {
      LogNoIpCountry(piId);
    }

    // Wrap entire flow in transaction for FOR UPDATE lock to work correctly
    await using var dbTx = await _db.BeginTransactionAsync(ct);

    var txMode = string.Equals(mode, "test", StringComparison.OrdinalIgnoreCase) ? ProviderMode.Test : ProviderMode.Live;

    // 1) Upsert transaction: ako postoji, koristi postoje?i; ako ne, kreiraj novi
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
        AmountMinor = amount,
        Currency = currency,
        CustomerEmail = customerEmail,
        CreatedUtc = createdUtc,
        Status = TransactionStatus.Insufficient,
        StatusReason = "Awaiting evidence"
      };

      _db.Transactions.Add(transaction);

      try
      {
        await _db.SaveChangesAsync(ct); // mora postojati u DB radi FOR UPDATE u AppendService
        LogTransactionCreated(transaction.Id, piId);
      }
      catch (DbUpdateException ex) when (IsTransactionUniqueViolation(ex))
      {
        // Parallel webhook created same transaction - load it and refresh missing fields
        transaction = await _db.Transactions.SingleAsync(x =>
          x.WorkspaceId == workspaceId &&
          x.Provider == ProviderKind.Stripe &&
          x.Mode == txMode &&
          x.ProviderTransactionId == piId, ct);

        // Refresh missing fields (same logic as else branch)
        if (transaction.ProviderChargeId is null && chargeId is not null) transaction.ProviderChargeId = chargeId;
        if (transaction.CustomerEmail is null && customerEmail is not null) transaction.CustomerEmail = customerEmail;
      }
    }
    else
    {
      // Optionally refresh charge/email if missing (safe, nije audit evidence)
      if (transaction.ProviderChargeId is null && chargeId is not null) transaction.ProviderChargeId = chargeId;
      if (transaction.CustomerEmail is null && customerEmail is not null) transaction.CustomerEmail = customerEmail;
    }

    // 2) Append billing evidence (if available) as fallback
    // Primary source is checkout.session.completed, but payment_intent can provide it too
    if (!string.IsNullOrWhiteSpace(billingCountry))
    {
      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Billingcountry,
          CountryCode: billingCountry,
          SourceRef: $"{piId}:billing",
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
        // Parallel webhook appended same evidence - this is OK (idempotent)
        _logger.LogInformation("Billing evidence already exists for Transaction={TransactionId}, SourceRef={SourceRef} (parallel webhook)", transaction.Id, $"{piId}:billing");
      }
    }

    // 3) Append IP evidence (only if available)
    if (!string.IsNullOrWhiteSpace(ipCountry))
    {
      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Ipcountry,
          CountryCode: ipCountry,
          SourceRef: $"{piId}:ip",
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
        // Parallel webhook appended same evidence - this is OK (idempotent)
        _logger.LogInformation("IP evidence already exists for Transaction={TransactionId}, SourceRef={SourceRef} (parallel webhook)", transaction.Id, $"{piId}:ip");
      }
    }

    // 3) Evaluate status from current evidence snapshot (robust vs out-of-order/retry)
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

  private static string? ExtractBillingCountry(JsonElement dataObj)
  {
    // Try billing_details.address.country (most common for PaymentIntent)
    if (dataObj.TryGetProperty("billing_details", out var billingDetails) &&
        billingDetails.ValueKind == JsonValueKind.Object &&
        billingDetails.TryGetProperty("address", out var address) &&
        address.ValueKind == JsonValueKind.Object &&
        address.TryGetProperty("country", out var country) &&
        country.ValueKind == JsonValueKind.String)
    {
      var countryCode = country.GetString()?.Trim().ToUpperInvariant();
      if (IsValidCountryCode(countryCode))
      {
        return countryCode;
      }
    }

    // Fallback: try metadata.billing_country
    if (dataObj.TryGetProperty("metadata", out var meta) &&
        meta.ValueKind == JsonValueKind.Object &&
        meta.TryGetProperty("billing_country", out var metaCountry) &&
        metaCountry.ValueKind == JsonValueKind.String)
    {
      var countryCode = metaCountry.GetString()?.Trim().ToUpperInvariant();
      if (IsValidCountryCode(countryCode))
      {
        return countryCode;
      }
    }

    return null;
  }

  private static string? ExtractIpCountry(JsonElement dataObj, string payloadJson, string? ipCountryHint)
  {
    // Option 0: Try ipCountryHint from controller (extracted from X-Forwarded-For or similar)
    if (!string.IsNullOrWhiteSpace(ipCountryHint))
    {
      var hintCode = ipCountryHint.Trim().ToUpperInvariant();
      if (IsValidCountryCode(hintCode))
      {
        return hintCode;
      }
    }

    // Option 1: Try metadata.ip_country (if you set it from frontend/Stripe Tax)
    if (dataObj.TryGetProperty("metadata", out var meta) &&
        meta.ValueKind == JsonValueKind.Object &&
        meta.TryGetProperty("ip_country", out var ipCountryMeta) &&
        ipCountryMeta.ValueKind == JsonValueKind.String)
    {
      var countryCode = ipCountryMeta.GetString()?.Trim().ToUpperInvariant();
      if (IsValidCountryCode(countryCode))
      {
        return countryCode;
      }
    }

    // Option 2: Try charges[0].billing_details.address.country (fallback for Charges API)
    if (dataObj.TryGetProperty("charges", out var charges) &&
        charges.ValueKind == JsonValueKind.Object &&
        charges.TryGetProperty("data", out var chargesData) &&
        chargesData.ValueKind == JsonValueKind.Array &&
        chargesData.GetArrayLength() > 0)
    {
      var firstCharge = chargesData[0];
      if (firstCharge.TryGetProperty("billing_details", out var billingDetails) &&
          billingDetails.ValueKind == JsonValueKind.Object &&
          billingDetails.TryGetProperty("address", out var address) &&
          address.ValueKind == JsonValueKind.Object &&
          address.TryGetProperty("country", out var country) &&
          country.ValueKind == JsonValueKind.String)
      {
        var countryCode = country.GetString()?.Trim().ToUpperInvariant();
        if (IsValidCountryCode(countryCode))
        {
          return countryCode;
        }
      }
    }

    // Option 3: Parse full JSON to check for Stripe Tax calculated_tax_amounts
    try
    {
      var fullDoc = JsonDocument.Parse(payloadJson);
      if (fullDoc.RootElement.TryGetProperty("data", out var dataRoot) &&
          dataRoot.TryGetProperty("object", out var obj) &&
          obj.TryGetProperty("automatic_tax", out var autoTax) &&
          autoTax.ValueKind == JsonValueKind.Object &&
          autoTax.TryGetProperty("location", out var location) &&
          location.ValueKind == JsonValueKind.Object &&
          location.TryGetProperty("country", out var taxCountry) &&
          taxCountry.ValueKind == JsonValueKind.String)
      {
        var countryCode = taxCountry.GetString()?.Trim().ToUpperInvariant();
        if (IsValidCountryCode(countryCode))
        {
          return countryCode;
        }
      }
    }
    catch
    {
      // Ignore parse errors
    }

    return null;
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
