using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
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
  private static readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
  private static DateTimeOffset? _lastHealthyTime = DateTimeOffset.UtcNow;
  private static DateTimeOffset? _downtimeStart = null;



  [HttpGet]
  public async Task<IActionResult> Get()
  {
    bool dbOk;
    string? dbProvider = null;
    List<string>? appliedMigrations = null;
    List<string>? pendingMigrations = null;

    try
    {
      dbOk = await _dbContext.Database.CanConnectAsync();

      if (dbOk)
      {
        dbProvider = _dbContext.Database.ProviderName;
        appliedMigrations = [.. await _dbContext.Database.GetAppliedMigrationsAsync()];
        pendingMigrations = [.. await _dbContext.Database.GetPendingMigrationsAsync()];
      }
    }
    catch (Exception)
    {
      dbOk = false;
    }

    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    // Try multiple sources for git commit SHA (support different CI/CD providers)
    var commit = _config["GIT_COMMIT_SHA"]        // Custom env var
              ?? _config["RENDER_GIT_COMMIT"]     // Render platform
              ?? _config["GITHUB_SHA"]            // GitHub Actions
              ?? "unknown";

    var uptime = DateTimeOffset.UtcNow - _startTime;

    // Track downtime
    TimeSpan? currentDowntime = null;
    if (dbOk)
    {
      _lastHealthyTime = DateTimeOffset.UtcNow;
      _downtimeStart = null;
    }
    else
    {
      if (_downtimeStart == null)
      {
        _downtimeStart = DateTimeOffset.UtcNow;
      }
      currentDowntime = DateTimeOffset.UtcNow - _downtimeStart.Value;
    }

    if (_env.IsDevelopment())
    {
      return Ok(new {});
    }
    else
    {
      return Ok(new
      {
        status = dbOk ? "OK" : "DEGRADED",
        isHealthy = dbOk,
        environment = _env.EnvironmentName,
        database = dbOk ? "Connected" : "Failed",
        databaseProvider = dbProvider ?? "Unknown",
        migrationsApplied = appliedMigrations?.Count ?? 0,
        migrations = appliedMigrations,
        pendingMigrations = pendingMigrations?.Count ?? 0,
        pendingMigrationsList = pendingMigrations,
        uptime = new 
        {
          days = uptime.Days,
          hours = uptime.Hours,
          minutes = uptime.Minutes,
          seconds = uptime.Seconds,
          totalSeconds = (int)uptime.TotalSeconds
        },
        downtime = dbOk ? (object)new
        {
          isDown = false,
          since = (DateTimeOffset?)null,
          duration = (object?)null
        } : new
        {
          isDown = true,
          since = _downtimeStart,
          duration = new
          {
            days = currentDowntime!.Value.Days,
            hours = currentDowntime.Value.Hours,
            minutes = currentDowntime.Value.Minutes,
            seconds = currentDowntime.Value.Seconds,
            totalSeconds = (int)currentDowntime.Value.TotalSeconds
          }
        },
        lastHealthyCheck = _lastHealthyTime,
        timestamp = DateTimeOffset.UtcNow,
        version = version ?? "1.0.0.0",
        commit
      });
    }
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

  /// <summary>
  /// Test endpoint to verify Cloudflare headers (CF-IPCountry) are being forwarded.
  /// Useful for debugging webhook IP country extraction.
  /// </summary>
  [HttpGet("headers")]
  public IActionResult Headers()
  {
    var cfIpCountry = Request.Headers["CF-IPCountry"].ToString();
    var cfConnectingIp = Request.Headers["CF-Connecting-IP"].ToString();
    var cfRay = Request.Headers["CF-RAY"].ToString();
    var xForwardedFor = Request.Headers["X-Forwarded-For"].ToString();
    var xRealIp = Request.Headers["X-Real-IP"].ToString();

    // All headers for debugging
    var allHeaders = Request.Headers
      .Where(h => h.Key.StartsWith("CF-", StringComparison.OrdinalIgnoreCase) ||
                  h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ||
                  h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
      .ToDictionary(h => h.Key, h => h.Value.ToString());

    return Ok(new
    {
      cloudflare = new
      {
        enabled = !string.IsNullOrWhiteSpace(cfIpCountry) || !string.IsNullOrWhiteSpace(cfRay),
        ipCountry = cfIpCountry,
        connectingIp = cfConnectingIp,
        ray = cfRay
      },
      forwarding = new
      {
        xForwardedFor,
        xRealIp
      },
      relevantHeaders = allHeaders,
      remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
      timestamp = DateTimeOffset.UtcNow
    });
  }
}
