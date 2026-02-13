namespace VatEvidence.Application.Webhooks;

public interface IStripeSignatureValidator
{
  bool Validate(string payload, string signatureHeader, string webhookSecret);
}
