using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace VatEvidence.Test.Integration.TestInfrastructure
{
  public class TestAppFactory : WebApplicationFactory<Program>
  {
    private readonly string _connectionString;

    public TestAppFactory(string connection) => _connectionString = connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.ConfigureAppConfiguration((context, config) =>
      {
        var dict = new Dictionary<string, string?>
        {
          ["ConnectionStrings:Default"] = _connectionString
        };
        config.AddInMemoryCollection(dict);
      });
    }
  }
}