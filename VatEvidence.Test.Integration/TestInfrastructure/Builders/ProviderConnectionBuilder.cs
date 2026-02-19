using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Test.Integration.TestInfrastructure.Builders
{
  public sealed class ProviderConnectionBuilder
  {
    private Guid _id = Guid.NewGuid();
    private Guid _workspaceId= TestGuids.WorkspaceId;
    private ProviderKind _provider = ProviderKind.Stripe;
    private ProviderMode _mode = ProviderMode.Test;
    private string _webhookSecret = "whsec_test_secret_123";
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    public static ProviderConnectionBuilder Default() => new();
    public ProviderConnectionBuilder WithId(Guid id) { _id = id; return this; }
    public ProviderConnectionBuilder WithWorkspaceId(Guid workspaceId) { _workspaceId = workspaceId; return this; }
    public ProviderConnectionBuilder WithProvider(ProviderKind provider) { _provider = provider; return this; }
    public ProviderConnectionBuilder WithMode(ProviderMode mode) { _mode = mode; return this; }
    public ProviderConnectionBuilder WithWebhookSecret(string webhookSecret) { _webhookSecret = webhookSecret; return this; }
    public ProviderConnectionBuilder WithCreatedAt(DateTimeOffset createdAt) { _createdAt = createdAt; return this; }

    public ProviderConnection Build() => new()  
    {
      Id = _id,
      WorkspaceId = _workspaceId,
      Provider = _provider,
      Mode = _mode,
      WebhookSecret = _webhookSecret,
      CreatedAt = _createdAt
    };
  }
}