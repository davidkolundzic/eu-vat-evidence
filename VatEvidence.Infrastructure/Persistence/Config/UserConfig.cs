using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VatEvidence.Domain;

namespace VatEvidence.Infrastructure.Persistence.Config
{
  public sealed partial class WorkspaceConfig
  {
    public sealed class UserConfig : IEntityTypeConfiguration<User>
    {
      public void Configure(EntityTypeBuilder<User> b)
      {
        b.ToTable("users");
        b.HasKey(x => x.Id);

        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();

        b.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasMany(x => x.Workspaces).WithOne(x => x.User).HasForeignKey(x => x.UserId);
      }
    }
  }
}