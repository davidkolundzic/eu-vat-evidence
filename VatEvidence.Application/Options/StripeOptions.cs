namespace VatEvidence.Application.Options;

public sealed class StripeOptions
{
  public const string SectionName = "Stripe";

  public string TestSecretKey { get; set; } = string.Empty;
  public string LiveSecretKey { get; set; } = string.Empty;
}
