using System.Security.Cryptography;
using System.Text;

namespace VatEvidence.Test.Integration.TestInfrastructure.Helpers
{
  public sealed class StripeTestHelpers
  {
    public static string CreateStripeSignatureHeader(
      string payload, 
      string secret, 
      long? timestamp = null)
    {
      var signedPayload = $"{timestamp}.{payload}";
      using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
      var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
      var signature = Convert.ToHexString(hash).ToLowerInvariant();

      return $"t={timestamp},v1={signature}";
    }
  }
}
