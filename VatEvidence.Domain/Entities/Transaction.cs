
namespace VatEvidence.Domain
{
  public sealed class Transaction
  {
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = default!;

    public ProviderKind Provider { get; set; }
    public ProviderMode Mode { get; set; }

    public string ProviderTransactionId { get; set; } = ""; // Stripe pi_...
    public string? ProviderChargeId { get; set; }           // Stripe ch_...

    // Preporuka: bigint minor units (cents) da izbjegneš decimal edge-case.
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "EUR";

    public string? CustomerEmail { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    public TransactionStatus Status { get; set; }
    public string? StatusReason { get; set; }

    public ICollection<EvidenceRecord> EvidenceRecords { get; set; } = new List<EvidenceRecord>();
  }
}
