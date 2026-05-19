using System.Text.Json;
using VatEvidence.Domain;

namespace VatEvidence.Application.Webhooks;

/// <summary>
/// Helper methods for extracting data from Stripe webhook payloads.
/// </summary>
public static class StripePayloadExtractor
{
  /// <summary>
  /// Extracts PaymentIntent ID from various Stripe webhook event types.
  /// Supports: payment_intent.*, checkout.session.*, charge.*
  /// </summary>
  public static string? ExtractPaymentIntentId(string eventType, string payloadJson)
  {
    try
    {
      var doc = JsonDocument.Parse(payloadJson);
      var dataObj = doc.RootElement.GetProperty("data").GetProperty("object");

      // payment_intent.* events -> data.object.id
      if (eventType.StartsWith("payment_intent.", StringComparison.OrdinalIgnoreCase))
      {
        if (dataObj.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
          return id.GetString();
        }
      }

      // checkout.session.* events -> data.object.payment_intent
      if (eventType.StartsWith("checkout.session.", StringComparison.OrdinalIgnoreCase))
      {
        if (dataObj.TryGetProperty("payment_intent", out var pi) && pi.ValueKind == JsonValueKind.String)
        {
          return pi.GetString();
        }
      }

      // charge.* events -> data.object.payment_intent
      if (eventType.StartsWith("charge.", StringComparison.OrdinalIgnoreCase))
      {
        if (dataObj.TryGetProperty("payment_intent", out var pi) && pi.ValueKind == JsonValueKind.String)
        {
          return pi.GetString();
        }
      }

      return null;
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  /// Creates audit-friendly snapshot for billing evidence.
  /// </summary>
  public static JsonDocument CreateBillingSnapshot(string chargeId, string country, global::Stripe.Address? address)
  {
    var snapshot = new
    {
      chargeId,
      country,
      postalCode = address?.PostalCode ?? string.Empty,
      city = address?.City ?? string.Empty,
      source = "stripe.latest_charge.billing_details"
    };

    var json = JsonSerializer.Serialize(snapshot);
    return JsonDocument.Parse(json);
  }

  /// <summary>
  /// Creates audit-friendly snapshot for billing evidence from canonical BillingAddressSnapshot.
  /// </summary>
  public static JsonDocument CreateBillingSnapshot(string chargeId, string country, Application.Stripe.BillingAddressSnapshot? address)
  {
    var snapshot = new
    {
      chargeId,
      country,
      postalCode = address?.PostalCode ?? string.Empty,
      city = address?.City ?? string.Empty,
      source = "stripe.latest_charge.billing_details"
    };

    var json = JsonSerializer.Serialize(snapshot);
    return JsonDocument.Parse(json);
  }

  /// <summary>
  /// Creates audit-friendly snapshot for IP evidence.
  /// </summary>
  public static JsonDocument CreateIpSnapshot(string country, string source, bool headerPresent)
  {
    var snapshot = new
    {
      country,
      source,
      headerPresent
    };

    var json = JsonSerializer.Serialize(snapshot);
    return JsonDocument.Parse(json);
  }
}
