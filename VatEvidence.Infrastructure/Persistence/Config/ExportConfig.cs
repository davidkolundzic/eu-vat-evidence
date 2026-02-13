using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed class ExportConfig : IEntityTypeConfiguration<Export>
  {
    public void Configure(EntityTypeBuilder<Export> b)
    {
      b.ToTable("exports");
      b.HasKey(x => x.Id);

      b.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();

      b.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
      b.Property(x => x.RangeFrom).HasColumnName("range_from").IsRequired();
      b.Property(x => x.RangeTo).HasColumnName("range_to").IsRequired();
      b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();

      b.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(600).IsRequired();
      b.Property(x => x.FileHash).HasColumnName("file_hash").HasMaxLength(64).IsRequired();

      b.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId);
    }
  }

}
