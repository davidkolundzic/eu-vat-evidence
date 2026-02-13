using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Test.Integration.TestInfrastructure;

namespace VatEvidence.Test.Integration.Smoke
{
  public sealed class DbResetCoverageTests : IntegrationTestBase, IClassFixture<LocalPostgresFixture>
  {
    public DbResetCoverageTests(LocalPostgresFixture postgresFixture) : base(postgresFixture)
    {
    }

    /// <summary>
    /// Verifies that the database reset operation includes all tables mapped by the Entity Framework model.
    /// </summary>
    /// <remarks>This test ensures that the SQL generated for resetting the database references every table
    /// mapped by the current Entity Framework model, including those using table-per-hierarchy (TPH) inheritance. The
    /// test helps maintain consistency between the application's data model and the database reset logic.</remarks>
    /// <returns></returns>
    [Fact(DisplayName= "DbReset: treba uključiti sve EF mapirane tablice")]
    public async Task DbReset_Should_IncludeAllEfMappedTables()
    {
      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

      // Expected: sve tablice koje EF model mapire (distinct zbog TPH)

      var expectedTables = db.Model.GetEntityTypes()
        .Where(et => !et.IsOwned())
        .Select(et => $"{et.GetSchema() ?? "public"}.{et.GetTableName()}")
        .Where(x => !x.EndsWith(".", StringComparison.Ordinal)) // safety ako je TableName null
        .Distinct() 
        .ToList();

      var sql = DbReset.GenerateSql(db);
      sql.Should().NotBeNull();

      // Provjeri da se svakak tablica pojavljuje u SQL-u
      foreach (var table in expectedTables)
      {
        var needle = table.Replace("public.", "\"public\".").Replace(".", "\".\"");
        sql!.Should().Contain(table.Split('.')[1], $"Expected SQL to include table {table}");
      }
    }
  }
}
