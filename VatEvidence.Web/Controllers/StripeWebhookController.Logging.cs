using Microsoft.Extensions.Logging;

namespace VatEvidence.Web.Controllers;

public sealed partial class StripeWebhookController
{
  [LoggerMessage(Level = LogLevel.Warning, Message = "Empty webhook payload received")]
  private partial void LogEmptyPayload();

  [LoggerMessage(Level = LogLevel.Warning, Message = "Missing Stripe-Signature header")]
  private partial void LogMissingSignature();

  [LoggerMessage(Level = LogLevel.Warning, Message = "Missing or invalid workspace_id query parameter")]
  private partial void LogMissingWorkspaceId();

  [LoggerMessage(Level = LogLevel.Warning, Message = "No provider connection found for workspace {WorkspaceId} mode {Mode}")]
  private partial void LogNoProviderConnection(Guid workspaceId, string mode);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid Stripe signature for workspace {WorkspaceId}")]
  private partial void LogInvalidSignature(Guid workspaceId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Successfully processed webhook {EventId} for workspace {WorkspaceId}")]
  private partial void LogWebhookProcessed(string eventId, Guid workspaceId);

  [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process webhook {EventId}: {Error}")]
  private partial void LogProcessingError(string eventId, string? error);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Retryable error processing webhook {EventId}, Stripe will retry")]
  private partial void LogRetryableError(string eventId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Non-retryable error processing webhook {EventId}, no retry needed")]
  private partial void LogNonRetryableError(string eventId);
}
