using System.Text.Json;


namespace VatEvidence.Domain
{
  public sealed class ProviderEvent
  {
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = default!;

    public ProviderKind Provider { get; set; } 
    public ProviderMode Mode { get; set; } 

    public string ProviderEventId { get; set; } = string.Empty; // Stipe evt_...
    public string Type { get; set; } = string.Empty; // payment_intent.succeeded...


    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ReceivedUtc { get; set; }

    public JsonDocument PayloadJson { get; set; } = default!;
    public string PayloadHash { get; set; } = string.Empty; // SHA256 hash of PayloadJson

    public EventProcessingStatus ProcessingStatus { get; set; }
    public string? Error { get; set; }
  }
}
