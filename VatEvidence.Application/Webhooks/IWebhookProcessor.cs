namespace VatEvidence.Application.Webhooks;

public interface IWebhookProcessor
{
  Task<WebhookProcessResult> ProcessAsync(ProcessWebhookCommand command, CancellationToken ct = default);
}

public sealed record WebhookProcessResult(
  bool Success,
  Guid? ProviderEventId,
  string? ErrorMessage,
  bool Retryable
);

