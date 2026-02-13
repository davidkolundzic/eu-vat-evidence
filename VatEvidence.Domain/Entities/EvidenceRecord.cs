using System.Text.Json;

namespace VatEvidence.Domain
{
  public sealed class EvidenceRecord
  {
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = default!;

    // Monotoni redni broj unutar transakcije (determinističan ordering za hash-chain)
    public long Sequence { get; set; }

    public DateTimeOffset CapturedUtc { get; set; }

    public EvidenceType EvidenceType { get; set; }
    public string CountryCode { get; set; } = ""; // ISO2

    public JsonDocument? ValueRaw { get; set; }   // jsonb (može i null)
    public string SourceRef { get; set; } = "";   // "stripe:test:evt_123"

    public string RecordHash { get; set; } = "";
    public string? PrevRecordHash { get; set; }
  }
}