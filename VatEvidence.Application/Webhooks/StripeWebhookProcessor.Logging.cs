using Microsoft.Extensions.Logging;

namespace VatEvidence.Application.Webhooks;

public sealed partial class StripeWebhookProcessor
{
  [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate event {EventId} already processed, skipping")]
  private partial void LogDuplicateEvent(string eventId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled event type: {EventType}")]
  private partial void LogUnhandledEventType(string eventType);

  [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process event {EventId}")]
  private partial void LogProcessingError(Exception ex, string eventId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "No billing country found in payment_intent {PaymentIntentId}")]
  private partial void LogNoBillingCountry(string paymentIntentId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "No IP country found in payment_intent {PaymentIntentId}")]
  private partial void LogNoIpCountry(string paymentIntentId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Created transaction {TransactionId} for PI {PaymentIntentId}")]
  private partial void LogTransactionCreated(Guid transactionId, string paymentIntentId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Appended billing country evidence {Country} for transaction {TransactionId}")]
  private partial void LogBillingCountryAppended(string country, Guid transactionId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Appended IP country evidence {Country} for transaction {TransactionId}")]
  private partial void LogIpCountryAppended(string country, Guid transactionId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Canonical fetch for PI {PaymentIntentId}: billing={BillingCountry}, ip={IpCountry}")]
  partial void LogCanonicalFetch(string paymentIntentId, string? billingCountry, string? ipCountry);

  [LoggerMessage(Level = LogLevel.Debug, Message = "Raw webhook payload for event {EventType}: {PayloadJson}")]
  private partial void LogRawWebhookPayload(string eventType, string payloadJson);

  [LoggerMessage(Level = LogLevel.Information, Message = "Received checkout.session.completed: session={SessionId}, payment_intent={PaymentIntentId}, billing_country={BillingCountry}")]
  private partial void LogCheckoutSessionReceived(string sessionId, string paymentIntentId, string? billingCountry);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Checkout session {SessionId} missing payment_intent, skipping")]
  private partial void LogCheckoutSessionMissingPaymentIntent(string sessionId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Checkout session {SessionId} (PI: {PaymentIntentId}) missing billing country in customer_details")]
  private partial void LogCheckoutSessionMissingBillingCountry(string sessionId, string paymentIntentId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate key violation during final commit for event {EventId}, continuing (parallel processing)")]
  private partial void LogDuplicateDuringFinalCommit(string eventId);
}
