using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using VatEvidence.Domain;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Test.Integration.TestInfrastructure;
using VatEvidence.Test.Integration.TestInfrastructure.Builders;

namespace VatEvidence.Test.Integration.Examples
{
  public sealed class ExampleIntegrationTests : IntegrationTestBase, IClassFixture<LocalPostgresFixture>
  {
    // - Svaki test dobija potpuno čistu bazu zahvaljujući LocalPostgresFixture i DbReset u IntegrationTestBase
    // - Factory i Client su spremni za korišćenje u svakom testu
    // - Ovaj primjer pokazuje kako organizirati testove i koristiti zajedničku infrastrukturu
    public ExampleIntegrationTests(LocalPostgresFixture postgresFixture) : base(postgresFixture) { }

    [Fact]
    public async Task Example_HomePage_ShouldReturn200()
    {
      // Arrange - Automatski čista baza za svaki test
      // Factory, Client i DbReset su već postavljeni u base klasi

      // Act
      var response = await Client.GetAsync("/");

      // Assert
      response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Example_CreateTransaction_ShouldSaveToDatabase()
    {
      // Arrange - Svaki test dobija potpuno čistu bazu
      // Kreiraj test podatke ovde...
      // Kreiraj test transakciju
      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

      var workspace = WorkspaceBuilder.Default().Build();
      var transaction = TransactionBuilder.Default()
         .ForWorkspaceId(workspace.Id)
         .WithProviderTransactionId("pi_test_123456")
         .Build();

      // Act - Sačuvaj transakciju u bazi
      db.Workspaces.Add(workspace);
      db.Transactions.Add(transaction);

      await db.SaveChangesAsync();

      // Assert
      // Verifikuj rezultate...

      var savedTransaction = await db.Transactions
        .Include(t => t.Workspace) // Uključuje povezani Workspace ako je potrebno
        .FirstOrDefaultAsync(t => t.Id == TestGuids.TransactionId);


      savedTransaction.Should().NotBeNull();
      savedTransaction.WorkspaceId.Should().Be(TestGuids.WorkspaceId);
      savedTransaction.AmountMinor.Should().Be(10050);
      savedTransaction.Currency.Should().Be(CurrencyCodes.EUR);
      savedTransaction.CustomerEmail.Should().Be("test@example.com");
      savedTransaction.Workspace.Name.Should().Be("Test Workspace");


    }
  }
}
