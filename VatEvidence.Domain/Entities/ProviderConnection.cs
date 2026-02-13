
namespace VatEvidence.Domain
{
  public sealed class ProviderConnection
  {
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = default!;
    public ProviderKind Provider { get; set; }    
    public ProviderMode Mode { get; set; }

    // MVP: spremi kako znas (kasnije enkriptiraj/KMS)
    public string WebhookSecret { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
  }
}