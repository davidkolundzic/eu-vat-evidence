using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed partial class WorkspaceConfig
  {
    public sealed class WorkspaceUserConfig : IEntityTypeConfiguration<WorkspaceUser>
    {
      public void Configure(EntityTypeBuilder<WorkspaceUser> b)
      {
        b.ToTable("workspace_users");
        b.HasKey(x => new { x.WorkspaceId, x.UserId });

        b.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();

        b.HasOne(x => x.Workspace).WithMany(x => x.Users).HasForeignKey(x => x.WorkspaceId);
        b.HasOne(x => x.User).WithMany(x => x.Workspaces).HasForeignKey(x => x.UserId);
      }
    }
  }
}