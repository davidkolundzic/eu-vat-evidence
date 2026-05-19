namespace VatEvidence.Application.Stripe;

/// <summary>
/// Service for fetching canonical Stripe PaymentIntent state via server-to-server API.
/// Abstracts Stripe.NET SDK calls from webhook processing pipeline.
/// </summary>
public interface IStripeCanonicalReader
{
  /// <summary>
  /// Fetches canonical PaymentIntent state from Stripe API.
  /// </summary>
  /// <param name="paymentIntentId">PaymentIntent ID (pi_xxx)</param>
  /// <param name="mode">Test or Live mode (determines which API key to use)</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>Canonical snapshot containing PaymentIntent + Charge billing details</returns>
  /// <exception cref="InvalidOperationException">Stripe API key not configured for specified mode</exception>
  /// <exception cref="Stripe.StripeException">Stripe API errors (404, timeout, rate limit, etc.)</exception>
  Task<CanonicalStripeSnapshot> ReadAsync(string paymentIntentId, StripeMode mode, CancellationToken ct = default);
}
