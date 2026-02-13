using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed class ProviderEventConfig : IEntityTypeConfiguration<ProviderEvent>
  {
    public void Configure(EntityTypeBuilder<ProviderEvent> b)
    {
      b.ToTable("provider_events");
      b.HasKey(x => x.Id);

      b.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();

      b.Property(x => x.Provider).HasColumnName("provider").HasConversion<int>().IsRequired();
      b.Property(x => x.Mode).HasColumnName("mode").HasConversion<int>().IsRequired();

      b.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(200).IsRequired();
      b.Property(x => x.Type).HasColumnName("type").HasMaxLength(200).IsRequired();

      b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
      b.Property(x => x.ReceivedUtc).HasColumnName("received_utc").IsRequired();

      b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
      b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();

      b.Property(x => x.ProcessingStatus).HasColumnName("processing_status").HasConversion<int>().IsRequired();
      b.Property(x => x.Error).HasColumnName("error");

      b.HasIndex(x => new { x.WorkspaceId, x.Provider, x.Mode, x.ProviderEventId }).IsUnique();

      b.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId);
    }
  }
}
