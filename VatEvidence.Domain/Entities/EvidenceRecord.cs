using System.Text.Json;

namespace VatEvidence.Domain
{
  public sealed class EvidenceRecord
  {
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = default!;

    // Monotonic sequence number within the transaction (deterministic ordering for the hash-chain)
    public long Sequence { get; set; }

    public DateTimeOffset CapturedUtc { get; set; }

    public EvidenceType EvidenceType { get; set; }
    public string CountryCode { get; set; } = ""; // ISO2

    public JsonDocument? ValueRaw { get; set; }   // jsonb (may be null)
    public string SourceRef { get; set; } = "";   // "stripe:test:evt_123"

    public string RecordHash { get; set; } = "";
    public string? PrevRecordHash { get; set; }
  }
}