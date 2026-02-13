using FluentAssertions;
using VatEvidence.Test.Integration.TestInfrastructure;

namespace VatEvidence.Test.Integration.Smoke
{
  public sealed class BootTests : IntegrationTestBase, IClassFixture<LocalPostgresFixture>
  {
    public BootTests(LocalPostgresFixture postgresFixture) : base(postgresFixture) { }

    [Fact]
    public async Task App_ShouldStart_AndMigrateDb()
    {
      // Factory, migracije i DbReset se automatski pozivaju u InitializeAsync()
      Client.Should().NotBeNull();
    }
  }
}
