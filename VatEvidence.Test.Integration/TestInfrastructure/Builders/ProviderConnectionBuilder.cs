using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Test.Integration.TestInfrastructure.Builders
{
  public sealed class ProviderConnectionBuilder
  {
    private Guid Id = Guid.NewGuid();
    private Guid WorkspaceId = TestGuids.WorkspaceId;
    private ProviderKind Provider = ProviderKind.Stripe;
    private ProviderMode Mode = ProviderMode.Test;
    private string WebhookSecret = "whsec_test_secret_123";
    private DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;

    public static ProviderConnectionBuilder Default() => new();
    public ProviderConnectionBuilder WithId(Guid id) { Id = id; return this; }
    public ProviderConnectionBuilder WithWorkspaceId(Guid workspaceId) { WorkspaceId = workspaceId; return this; }
    public ProviderConnectionBuilder WithProvider(ProviderKind provider) { Provider = provider; return this; }
    public ProviderConnectionBuilder WithMode(ProviderMode mode) { Mode = mode; return this; }
    public ProviderConnectionBuilder WithWebhookSecret(string webhookSecret) { WebhookSecret = webhookSecret; return this; }
    public ProviderConnectionBuilder WithCreatedAt(DateTimeOffset createdAt) { CreatedAt = createdAt; return this; }

    public ProviderConnection Build() => new ProviderConnection
    {
      Id = Id,
      WorkspaceId = WorkspaceId,
      Provider = Provider,
      Mode = Mode,
      WebhookSecret = WebhookSecret,
      CreatedAt = CreatedAt
    };
  }
}