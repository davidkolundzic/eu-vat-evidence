using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed class ProviderConnectionConfig : IEntityTypeConfiguration<ProviderConnection>
  {
    public void Configure(EntityTypeBuilder<ProviderConnection> b)
    {
      b.ToTable("provider_connections");
      b.HasKey(x => x.Id);

      b.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();

      b.Property(x => x.Provider).HasColumnName("provider").HasConversion<int>().IsRequired();
      b.Property(x => x.Mode).HasColumnName("mode").HasConversion<int>().IsRequired();

      b.Property(x => x.WebhookSecret).HasColumnName("webhook_secret").IsRequired();
      b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

      b.HasIndex(x => new { x.WorkspaceId, x.Provider, x.Mode }).IsUnique();

      b.HasOne(x => x.Workspace).WithMany(x => x.ProviderConnections).HasForeignKey(x => x.WorkspaceId);
    }
  }
}
