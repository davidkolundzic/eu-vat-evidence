using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VatEvidence.Infrastructure.Persistence;

namespace VatEvidence.Test.Integration.TestInfrastructure
{
  public static class DbMigrations
  {
      public static async Task Migrate(IServiceProvider serviceProvider)
      {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
  }
}
