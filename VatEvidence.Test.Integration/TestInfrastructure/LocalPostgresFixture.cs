using Npgsql;

namespace VatEvidence.Test.Integration.TestInfrastructure
{
  public sealed class LocalPostgresFixture : IAsyncLifetime
  {
    private const string TestDatabaseName = "VatEvidenceTest";
    
    // Koristi POSEBNU test bazu - ne diraju se production podaci!
    public string ConnectionString =>
      $"Host=localhost;Port=5433;Database={TestDatabaseName};Username=postgres;Password=MaliMedo11";

    public async Task InitializeAsync()
    {
      // Kreiraj test bazu ako ne postoji
      var masterConnectionString = "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=MaliMedo11";
      
      await using var connection = new NpgsqlConnection(masterConnectionString);
      await connection.OpenAsync();

      // Proveri da li baza postoji
      await using var cmd = new NpgsqlCommand(
        $"SELECT 1 FROM pg_database WHERE datname = '{TestDatabaseName}'", 
        connection);
      
      var exists = await cmd.ExecuteScalarAsync();
      
      if (exists == null)
      {
        // Kreiraj bazu
        await using var createCmd = new NpgsqlCommand(
          $"CREATE DATABASE \"{TestDatabaseName}\"", 
          connection);
        await createCmd.ExecuteNonQueryAsync();
      }
    }

    public Task DisposeAsync() => Task.CompletedTask;
  }
}
