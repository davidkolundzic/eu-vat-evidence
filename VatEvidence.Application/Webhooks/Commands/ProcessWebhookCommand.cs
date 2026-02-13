namespace VatEvidence.Application.Webhooks;

public sealed record ProcessWebhookCommand(
  Guid WorkspaceId,
  string Provider,      // "stripe"
  string Mode,          // "test" / "live"
  string EventId,       // "evt_..."
  string EventType,     // "payment_intent.succeeded"
  DateTimeOffset CreatedUtc,
  string PayloadJson    // Raw JSON string
);
