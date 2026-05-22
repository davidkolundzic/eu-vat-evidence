using System.Threading.Tasks;

namespace Testcontainers.PostgreSql
{
  // Lightweight stub implementations to allow the test project to compile when the
  // external DotNet.Testcontainers package is not available in the environment.
  // These are no-op and intended only to satisfy build; they do not provide
  // production-quality container lifecycle management.

  public sealed class PostgreSqlBuilder
  {
    private string _image = "postgres:latest";
    private string _database = "test";
    private string _username = "user";
    private string _password = "pass";
    private bool _cleanUp = false;

    public PostgreSqlBuilder WithImage(string image) { _image = image; return this; }
    public PostgreSqlBuilder WithDatabase(string database) { _database = database; return this; }
    public PostgreSqlBuilder WithUsername(string username) { _username = username; return this; }
    public PostgreSqlBuilder WithPassword(string password) { _password = password; return this; }
    public PostgreSqlBuilder WithCleanUp(bool cleanUp) { _cleanUp = cleanUp; return this; }

    public PostgreSqlContainer Build() => new PostgreSqlContainer(_image, _database, _username, _password, _cleanUp);
  }

  public sealed class PostgreSqlContainer
  {
    private readonly string _connectionString;

    public PostgreSqlContainer(string image, string database, string username, string password, bool cleanUp)
    {
      // Return a reasonable default connection string pointing to localhost so tests
      // that only compile can access a string. Integration tests that actually require
      // a running container should use the real Testcontainers package.
      _connectionString = $"Host=localhost;Port=5432;Database={database};Username={username};Password={password}";
    }

    public string GetConnectionString() => _connectionString;

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;
  }
}
