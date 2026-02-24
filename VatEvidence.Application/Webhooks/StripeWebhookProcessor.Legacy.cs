using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VatEvidence.Application.Evidence;
using VatEvidence.Domain;

namespace VatEvidence.Application.Webhooks;

/// <summary>
/// Legacy webhook processing methods (no longer used).
/// These methods parse webhook payloads directly instead of using canonical Stripe API fetch.
/// Kept temporarily for reference during migration, will be removed in future version.
/// </summary>
public sealed partial class StripeWebhookProcessor
{
  [Obsolete("Legacy code. Use ProcessStripeTransactionAsync with canonical Stripe API fetch instead.")]
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

    // Extract IP country from hint (checkout session doesn't contain IP data directly)
    string? ipCountry = null;
    if (!string.IsNullOrWhiteSpace(ipCountryHint))
    {
      var hintCode = ipCountryHint.Trim().ToUpperInvariant();
      if (IsValidCountryCode(hintCode))
      {
        ipCountry = hintCode;
      }
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

  [Obsolete("Legacy code. Use ProcessStripeTransactionAsync with canonical Stripe API fetch instead.")]
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

    // 1) Upsert transaction: ako postoji, koristi postojeći; ako ne, kreiraj novi
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

  [Obsolete("Legacy helper. Use canonical Stripe API fetch instead (paymentIntent.LatestCharge.BillingDetails.Address.Country).")]
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

  [Obsolete("Legacy helper. Use ipCountryHint parameter directly (extracted from CF-IPCountry header).")]
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
}
