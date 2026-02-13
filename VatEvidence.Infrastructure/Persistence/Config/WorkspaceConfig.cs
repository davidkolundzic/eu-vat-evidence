using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed partial class WorkspaceConfig : IEntityTypeConfiguration<Workspace>
  {
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
      builder.ToTable("workspaces");
      builder.HasKey(w => w.Id);


      builder.Property(w => w.Name)
             .IsRequired()
             .HasMaxLength(200);

      builder.Property(w => w.CreatedAt)
              .IsRequired();

      builder.HasMany(x => x.Users)
        .WithOne(x => x.Workspace)
        .HasForeignKey(x => x.WorkspaceId);

      builder.HasMany(x => x.ProviderConnections)
        .WithOne(x => x.Workspace)
        .HasForeignKey(x => x.WorkspaceId);
    }
  }
}