namespace VatEvidence.Application.Stripe;

/// <summary>
/// Minimal billing address snapshot extracted from Stripe Charge.BillingDetails.Address.
/// </summary>
public sealed class BillingAddressSnapshot
{
  public string? Line1 { get; init; }
  public string? Line2 { get; init; }
  public string? City { get; init; }
  public string? PostalCode { get; init; }
  public string? State { get; init; }
  public string? Country { get; init; }
}
