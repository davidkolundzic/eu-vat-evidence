using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed class TransactionConfig : IEntityTypeConfiguration<Transaction>
  {
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
      b.ToTable("transactions");
      b.HasKey(x => x.Id);

      b.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();

      b.Property(x => x.Provider).HasColumnName("provider").HasConversion<int>().IsRequired();
      b.Property(x => x.Mode).HasColumnName("mode").HasConversion<int>().IsRequired();

      b.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(200).IsRequired();
      b.Property(x => x.ProviderChargeId).HasColumnName("provider_charge_id").HasMaxLength(200);

      b.Property(x => x.AmountMinor).HasColumnName("amount_minor").IsRequired();
      b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

      b.Property(x => x.CustomerEmail).HasColumnName("customer_email").HasMaxLength(320);
      b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();

      b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
      b.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(500);

      b.HasIndex(x => new { x.WorkspaceId, x.Provider, x.Mode, x.ProviderTransactionId }).IsUnique();

      b.HasMany(x => x.EvidenceRecords).WithOne(x => x.Transaction).HasForeignKey(x => x.TransactionId);

      b.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId);
    }
  }

}
