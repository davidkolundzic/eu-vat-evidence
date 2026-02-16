using Microsoft.AspNetCore.Mvc;
using VatEvidence.Infrastructure.Persistence;

namespace VatEvidence.Web.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(
  AppDbContext _dbContext,
  IWebHostEnvironment _env) : ControllerBase
{

    

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow });
    }

  [HttpGet]
  public IActionResult Info()
  {
    return Ok( new { environment = _env.EnvironmentName });
  }

  [HttpGet("db")]
  public async Task<IActionResult> TestDb()
  {
    var canConnect = await _dbContext.Database.CanConnectAsync();
    return Ok(new {database = canConnect ? "Connected" : "Failed"});
  }
}
