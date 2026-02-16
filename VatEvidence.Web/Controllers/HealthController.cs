using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using VatEvidence.Infrastructure.Persistence;

namespace VatEvidence.Web.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(
  AppDbContext _dbContext,
  IWebHostEnvironment _env,
  IConfiguration _config) : ControllerBase
{



  [HttpGet]
  public async Task<IActionResult> Get()
  {
    bool dbOk;

    try
    {
      dbOk = await _dbContext.Database.CanConnectAsync();
    }
    catch (Exception)
    {

      dbOk = false;
    }

    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
    var commit = _config["GIT_COMMIT_SHA"];

    return Ok(new 
    { 
      status = "healthy", 
      timestamp = DateTimeOffset.UtcNow 
    });
  }

  [HttpGet("info")]
  public IActionResult Info()
  {
   

    return Ok(new { environment = _env.EnvironmentName });
  }

  [HttpGet("db")]
  public async Task<IActionResult> TestDb()
  {
    var canConnect = await _dbContext.Database.CanConnectAsync();
    return Ok(new { database = canConnect ? "Connected" : "Failed" });
  }
}
