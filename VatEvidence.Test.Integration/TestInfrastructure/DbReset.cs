using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using VatEvidence.Infrastructure.Persistence;

namespace VatEvidence.Test.Integration.TestInfrastructure
{
  public static class DbReset
  {
    public static async Task Reset(IServiceProvider serviceProvider)
    {
      using var scope = serviceProvider.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      // Old code manually named tables and restarted identities, but that was fragile and easy to forget when adding a new table
      //await db.Database.ExecuteSqlRawAsync(@"
      //  TRUNCATE TABLE 
      //    provider_events, 
      //    evidence_records, 
      //    transactions,
      //    provider_connections,
      //    provider_events,
      //    workspace_users,
      //    users,
      //    workspaces,
      //  RESTART IDENTITY CASCADE;
      //");

      // The new code uses DbContext to find all DbSet entities and deletes their data, then restarts identities
      var sql = GenerateSql(db);
      if (!string.IsNullOrWhiteSpace(sql))
      {
        await db.Database.ExecuteSqlRawAsync(sql);

      }
    }

    public static string GenerateSql(AppDbContext db)
    {
      var tables = db.Model.GetEntityTypes()
       // Ignore owned entity types because they don't have their own tables
       // skip EF Core internal / keyless / view mapping
       .Where(et => !et.IsOwned())
       .Select(et => new
       {
         Schema = et.GetSchema() ?? "public",
         Table = et.GetTableName()
       })
       .Where(x => !string.IsNullOrWhiteSpace(x.Table))
       // DISTINCT because TPH can return multiple entity types for the same table
       .Distinct()
       .ToList();

      if (tables.Count == 0) return string.Empty;

      // Quote schema + table (for snake_case, reserved words, mixed case)
      static string Q(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

      var sb = new StringBuilder();
      sb.Append("TRUNCATE TABLE");


      sb.AppendJoin(", ", tables.Select(t => $" {Q(t.Schema)}.{Q(t.Table!)}"));

      sb.Append(" RESTART IDENTITY CASCADE;");
      return sb.ToString();
    }
  }
}
