namespace VatEvidence.Application.Stripe;

/// <summary>
/// Canonical snapshot of Stripe PaymentIntent state fetched via server-to-server API.
/// Provides stable, reliable data independent of webhook event payload variations.
/// </summary>
public sealed class CanonicalStripeSnapshot
{
  public required string PiId { get; init; }
  public string? ChargeId { get; init; }
  public long AmountMinor { get; init; }
  public required string Currency { get; init; }
  public string? CustomerEmail { get; init; }
  public string? BillingCountry { get; init; }
  public BillingAddressSnapshot? BillingAddress { get; init; }
  public DateTimeOffset CreatedUtc { get; init; }
}
