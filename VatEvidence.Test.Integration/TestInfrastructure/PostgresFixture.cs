using Testcontainers.PostgreSql;

namespace VatEvidence.Test.Integration.TestInfrastructure
{
  public sealed class PostgresFixture : IAsyncLifetime
  {
    private readonly PostgreSqlContainer _container;

    public PostgresFixture()
    { 
      _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vatevidencetest")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .WithCleanUp(true)
        .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public  Task InitializeAsync() =>  _container.StartAsync();

    public  Task DisposeAsync() =>  _container.StopAsync();

  }
}
