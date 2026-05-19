using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using VatEvidence.Application.Options;

namespace VatEvidence.Application.Stripe;

/// <summary>
/// Implementation of IStripeCanonicalReader using Stripe.NET SDK.
/// Fetches PaymentIntent with expanded latest_charge for complete billing details.
/// Supports Test and Live mode via StripeOptions configuration.
/// Thread-safe, suitable for scoped or singleton registration (currently scoped for safety).
/// </summary>
public sealed class StripeCanonicalReader : IStripeCanonicalReader
{
  private readonly ILogger<StripeCanonicalReader> _logger;
  private readonly StripeOptions _stripeOptions;

  public StripeCanonicalReader(
    ILogger<StripeCanonicalReader> logger,
    IOptions<StripeOptions> stripeOptions)
  {
    _logger = logger;
    _stripeOptions = stripeOptions.Value;
  }

  public async Task<CanonicalStripeSnapshot> ReadAsync(
    string paymentIntentId,
    StripeMode mode,
    CancellationToken ct = default)
  {
    // 1) Select API key based on mode
    var apiKey = mode switch
    {
      StripeMode.Test => _stripeOptions.TestSecretKey,
      StripeMode.Live => _stripeOptions.LiveSecretKey,
      _ => throw new ArgumentException($"Unknown StripeMode: {mode}", nameof(mode))
    };

    if (string.IsNullOrWhiteSpace(apiKey))
    {
      throw new InvalidOperationException($"Stripe API key not configured for mode: {mode}");
    }

    // 2) Fetch PaymentIntent with expanded latest_charge
    var requestOptions = new RequestOptions { ApiKey = apiKey };
    var piService = new PaymentIntentService();

    PaymentIntent paymentIntent;
    try
    {
      paymentIntent = await piService.GetAsync(paymentIntentId, new PaymentIntentGetOptions
      {
        Expand = ["latest_charge"]
      }, requestOptions, ct);
    }
    catch (StripeException ex)
    {
      // 404 = PaymentIntent doesn't exist (non-retryable)
      if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound ||
          ex.StripeError?.Code == "resource_missing")
      {
        _logger.LogWarning(ex, "PaymentIntent {PaymentIntentId} not found in Stripe (mode={Mode}). Non-retryable.", paymentIntentId, mode);
        throw;
      }

      // Other errors (timeout, rate limit, 5xx) are retryable
      _logger.LogError(ex, "Failed to fetch PaymentIntent {PaymentIntentId} from Stripe API (mode={Mode}). Retryable.", paymentIntentId, mode);
      throw;
    }

    // 3) Extract data from PaymentIntent and latest_charge
    string? chargeId = null;
    string? billingCountry = null;
    string? customerEmail = null;
    BillingAddressSnapshot? billingAddress = null;

    if (paymentIntent.LatestCharge is Charge charge)
    {
      chargeId = charge.Id;

      // Extract customer email (prefer charge billing_details, fallback to receipt_email)
      if (!string.IsNullOrWhiteSpace(charge.BillingDetails?.Email))
      {
        customerEmail = charge.BillingDetails.Email;
      }
      else if (!string.IsNullOrWhiteSpace(paymentIntent.ReceiptEmail))
      {
        customerEmail = paymentIntent.ReceiptEmail;
      }

      // Extract billing country and address
      if (charge.BillingDetails?.Address is { } address)
      {
        if (!string.IsNullOrWhiteSpace(address.Country) && IsValidCountryCode(address.Country))
        {
          billingCountry = address.Country.Trim().ToUpperInvariant();
        }

        billingAddress = new BillingAddressSnapshot
        {
          Line1 = address.Line1,
          Line2 = address.Line2,
          City = address.City,
          PostalCode = address.PostalCode,
          State = address.State,
          Country = address.Country
        };
      }
    }

    // Fallback email if charge didn't have one
    customerEmail ??= paymentIntent.ReceiptEmail;

    // 4) Build canonical snapshot
    // Use AmountReceived if available (actual captured amount), otherwise use Amount (intended amount)
    var amountMinor = paymentIntent.AmountReceived > 0
      ? paymentIntent.AmountReceived
      : paymentIntent.Amount;

    var createdUtc = new DateTimeOffset(DateTime.SpecifyKind(paymentIntent.Created, DateTimeKind.Utc));

    return new CanonicalStripeSnapshot
    {
      PiId = paymentIntent.Id,
      ChargeId = chargeId,
      AmountMinor = amountMinor,
      Currency = paymentIntent.Currency?.ToUpperInvariant() ?? "EUR",
      CustomerEmail = customerEmail,
      BillingCountry = billingCountry,
      BillingAddress = billingAddress,
      CreatedUtc = createdUtc
    };
  }

  private static bool IsValidCountryCode(string? code)
  {
    // Simple validation: must be 2-letter uppercase code
    return code is { Length: 2 } && char.IsLetter(code[0]) && char.IsLetter(code[1]);
  }
}
