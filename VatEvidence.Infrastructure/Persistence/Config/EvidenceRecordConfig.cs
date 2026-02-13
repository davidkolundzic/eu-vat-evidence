using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed class EvidenceRecordConfig : IEntityTypeConfiguration<EvidenceRecord>
  {
    public void Configure(EntityTypeBuilder<EvidenceRecord> b)
    {
      b.ToTable("evidence_records");
      b.HasKey(x => x.Id);

      b.Property(x => x.TransactionId).HasColumnName("transaction_id").IsRequired();
      b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
      b.Property(x => x.CapturedUtc).HasColumnName("captured_utc").IsRequired();

      b.Property(x => x.EvidenceType).HasColumnName("evidence_type").HasConversion<int>().IsRequired();
      b.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();

      b.Property(x => x.ValueRaw).HasColumnName("value_raw").HasColumnType("jsonb");
      b.Property(x => x.SourceRef).HasColumnName("source_ref").HasMaxLength(300).IsRequired();

      b.Property(x => x.RecordHash).HasColumnName("record_hash").HasMaxLength(64).IsRequired();
      b.Property(x => x.PrevRecordHash).HasColumnName("prev_record_hash").HasMaxLength(64);

      // Determinističan ordering i brz tail lookup
      b.HasIndex(x => new { x.TransactionId, x.Sequence })
        .IsUnique()
        .HasDatabaseName("ux_evidence_records_tx_sequence");

      // Idempotency zaštita (isti event ne smije duplicirati evidencu)
      b.HasIndex(x => new { x.TransactionId, x.EvidenceType, x.SourceRef })
        .IsUnique()
        .HasDatabaseName("ux_evidence_records_tx_type_source");

      b.HasIndex(x => new { x.TransactionId, x.CapturedUtc })
        .HasDatabaseName("ix_evidence_records_transaction_id_captured_utc");

      b.HasOne(x => x.Transaction)
        .WithMany(x => x.EvidenceRecords)
        .HasForeignKey(x => x.TransactionId)
        // Append-only: evidence se nikad ne briše pojedinačno. Brisanje tx mora biti onemogućeno.
        .OnDelete(DeleteBehavior.Restrict);
    }
  }

}
