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

public sealed class StripeWebhookProcessor(
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
      _logger.LogInformation("Duplicate event {EventId} already processed, skipping", cmd.EventId);
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
      else
      {
        _logger.LogWarning("Unhandled event type: {EventType}", cmd.EventType);
      }

      // Mark as processed
      providerEvent.ProcessingStatus = EventProcessingStatus.Processed;
      await _db.SaveChangesAsync(ct);

      return new WebhookProcessResult(true, providerEvent.Id, null, false);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to process event {EventId}", cmd.EventId);
      
      providerEvent.ProcessingStatus = EventProcessingStatus.Failed;
      providerEvent.Error = ex.Message;
      await _db.SaveChangesAsync(ct);

      var retryable = IsRetryable(ex);
      return new WebhookProcessResult(false, providerEvent.Id, ex.Message, retryable);
    }
  }

  private async Task<ProviderEvent> SaveOrLoadEventAsync(ProcessWebhookCommand cmd, CancellationToken ct)
  {
    var providerKind = cmd.Provider.ToLowerInvariant() == "stripe" 
      ? ProviderKind.Stripe 
      : throw new ArgumentException($"Unknown provider: {cmd.Provider}");

    var mode = cmd.Mode.ToLowerInvariant() == "test" 
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
      // Duplicate event: u?itaj postoje?i iz DB i vrati ga (ne skipaj ako je Failed/Received)
      var existing = await _db.ProviderEvents
        .SingleAsync(x =>
          x.WorkspaceId == cmd.WorkspaceId &&
          x.Provider == providerKind &&
          x.Mode == mode &&
          x.ProviderEventId == cmd.EventId, ct);

      return existing;
    }
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
      _logger.LogWarning("No billing country found in payment_intent {PaymentIntentId}", piId);
    }

    // Extract IP country from hint, metadata, or charges
    var ipCountry = ExtractIpCountry(dataObj, payloadJson, ipCountryHint);
    if (string.IsNullOrWhiteSpace(ipCountry))
    {
      _logger.LogWarning("No IP country found in payment_intent {PaymentIntentId}", piId);
    }

    // Wrap entire flow in transaction for FOR UPDATE lock to work correctly
    await using var dbTx = await _db.BeginTransactionAsync(ct);

    var txMode = mode.ToLowerInvariant() == "test" ? ProviderMode.Test : ProviderMode.Live;

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
      await _db.SaveChangesAsync(ct); // mora postojati u DB radi FOR UPDATE u AppendService
      _logger.LogInformation("Created transaction {TransactionId} for PI {PaymentIntentId}", transaction.Id, piId);
    }
    else
    {
      // Optionally refresh charge/email if missing (safe, nije audit evidence)
      if (transaction.ProviderChargeId is null && chargeId is not null) transaction.ProviderChargeId = chargeId;
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
          SourceRef: $"{providerEventId}:billing",
          CapturedUtc: receivedUtc
        ),
        ct);
      await _db.SaveChangesAsync(ct);
      _logger.LogInformation("Appended billing country evidence {Country} for transaction {TransactionId}", billingCountry, transaction.Id);
    }

    // 3) Append IP evidence (only if available)
    if (!string.IsNullOrWhiteSpace(ipCountry))
    {
      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Ipcountry,
          CountryCode: ipCountry,
          SourceRef: $"{providerEventId}:ip",
          CapturedUtc: receivedUtc
        ),
        ct);
      await _db.SaveChangesAsync(ct);
      _logger.LogInformation("Appended IP country evidence {Country} for transaction {TransactionId}", ipCountry, transaction.Id);
    }

    // 4) Evaluate status from current evidence snapshot (robust vs out-of-order/retry)
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

    var billing = latest.SingleOrDefault(x => x.EvidenceType == EvidenceType.Billingcountry)?.CountryCode;
    var ip = latest.SingleOrDefault(x => x.EvidenceType == EvidenceType.Ipcountry)?.CountryCode;

    if (string.IsNullOrWhiteSpace(billing) || string.IsNullOrWhiteSpace(ip))
    {
      return (TransactionStatus.Insufficient, "Missing required evidence (billing/ip)");
    }

    if (!string.Equals(billing, ip, StringComparison.OrdinalIgnoreCase))
    {
      return (TransactionStatus.Mismatch, $"Evidence mismatch (billing={billing}, ip={ip})");
    }

    return (TransactionStatus.Ok, "Evidence OK (billing matches IP)");
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

  private static bool IsValidCountryCode(string? code)
  {
    // Simple validation: must be 2-letter uppercase code
    return code is { Length: 2 } && char.IsLetter(code[0]) && char.IsLetter(code[1]);
  }
}
