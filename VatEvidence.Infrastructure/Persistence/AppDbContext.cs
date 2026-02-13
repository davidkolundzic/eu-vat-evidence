using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Application.Interfaces;
using VatEvidence.Domain;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace VatEvidence.Infrastructure.Persistence
{
  public sealed class AppDbContext : DbContext, IAppDbContext
  {
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<User>  Users => Set<User>();
    public DbSet<WorkspaceUser> WorkspaceUsers => Set<WorkspaceUser>();
    public DbSet<ProviderConnection> ProviderConnections => Set<ProviderConnection>();
    public DbSet<ProviderEvent> ProviderEvents => Set<ProviderEvent>();

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<EvidenceRecord> EvidenceRecords => Set<EvidenceRecord>();

    public DbSet<Export> Exports => Set<Export>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      // Apply all configurations from the current assembly 
      // (i.e., configurations implementing IEntityTypeConfiguration<T>)
      // This keeps the model configuration organized and modular.
      modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
      base.OnModelCreating(modelBuilder);
    }

    Task<IDbContextTransaction> IAppDbContext.BeginTransactionAsync(CancellationToken ct)
        => Database.BeginTransactionAsync(ct);

    public IQueryable<T> FromSqlInterpolated<T>(FormattableString sql) where T : class
        => Set<T>().FromSqlInterpolated(sql);
  }

}

