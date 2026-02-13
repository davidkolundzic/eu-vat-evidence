namespace VatEvidence.Test.Integration.TestInfrastructure
{
  public abstract class IntegrationTestBase :  IAsyncLifetime
  {
    protected readonly LocalPostgresFixture PostgresFixture;
    protected TestAppFactory Factory = null!;
    protected HttpClient Client = null!;

    protected IntegrationTestBase(LocalPostgresFixture postgresFixture)
    {
      PostgresFixture = postgresFixture;
    }

    public async Task InitializeAsync()
    {
      Factory = new TestAppFactory(PostgresFixture.ConnectionString);
      await DbMigrations.Migrate(Factory.Services);
      await DbReset.Reset(Factory.Services);
      Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
      await Factory.DisposeAsync();
    }
  }
}
