using Stripe;

namespace VatEvidence.Application.Webhooks;

public sealed class StripeSignatureValidator : IStripeSignatureValidator
{
  public bool Validate(string payload, string signatureHeader, string webhookSecret)
  {
    try
    {
      // Parse signature header
      var signatureParts = signatureHeader.Split(',');
      var timestamp = signatureParts.FirstOrDefault(p => p.StartsWith("t="))?.Substring(2);
      var signature = signatureParts.FirstOrDefault(p => p.StartsWith("v1="))?.Substring(3);

      if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
      {
        return false;
      }

      // Compute expected signature
      var signedPayload = $"{timestamp}.{payload}";
      using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(webhookSecret));
      var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signedPayload));
      var expectedSignature = Convert.ToHexString(hash).ToLowerInvariant();

      // Compare signatures
      return signature == expectedSignature;
    }
    catch (Exception)
    {
      return false;
    }
  }
}
