using Microsoft.EntityFrameworkCore;
using VatEvidence.Domain;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace VatEvidence.Application.Interfaces;

public interface IAppDbContext
{
  DbSet<Workspace> Workspaces { get; }
  DbSet<User> Users { get; }
  DbSet<WorkspaceUser> WorkspaceUsers { get; }
  DbSet<ProviderConnection> ProviderConnections { get; }
  DbSet<ProviderEvent> ProviderEvents { get; }
  DbSet<Transaction> Transactions { get; }
  DbSet<EvidenceRecord> EvidenceRecords { get; }
  DbSet<Export> Exports { get; }

  Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
  Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
  
  // Raw SQL query support for complex scenarios (e.g., FOR UPDATE locks)
  IQueryable<T> FromSqlInterpolated<T>(FormattableString sql) where T : class;
}
