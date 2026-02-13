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
      // Stari kod rucno imenovanje tabela i restartovanje identiteta, ali to je krhko i lako se zaboravi da se doda nova tabela
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

      // Novi kod koristi DbContext da nadje sve DbSet-ove i obrise podatke iz njih, a zatim restartuje identitet
      var sql = GenerateSql(db);
      if (!string.IsNullOrWhiteSpace(sql))
      {
        await db.Database.ExecuteSqlRawAsync(sql);

      }
    }

    public static string GenerateSql(AppDbContext db)
    {
      var tables = db.Model.GetEntityTypes()
       // Ignoriši owned entity types jer oni nemaju svoje tabele
       // preskoči EF core internal / keyless / view mapping
       .Where(et => !et.IsOwned())
       .Select(et => new
       {
         Schema = et.GetSchema() ?? "public",
         Table = et.GetTableName()
       })
       .Where(x => !string.IsNullOrWhiteSpace(x.Table))
       // DISTINCT jer TPH može vratiti više entity tipova za istu tablicu
       .Distinct()
       .ToList();

      if (tables.Count == 0) return string.Empty;

      // Quote schema + table (za slučaj snake_case, reserved words, mixed case)
      static string Q(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

      var sb = new StringBuilder();
      sb.Append("TRUNCATE TABLE");


      sb.AppendJoin(", ", tables.Select(t => $" {Q(t.Schema)}.{Q(t.Table!)}"));

      sb.Append(" RESTART IDENTITY CASCADE;");
      return sb.ToString();
    }
  }
}
