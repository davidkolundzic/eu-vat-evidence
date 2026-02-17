using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VatEvidence.Application.Interfaces;
using VatEvidence.Application.Webhooks;
using VatEvidence.Domain;

namespace VatEvidence.Web.Controllers;

[ApiController]
[Route("api/webhooks/stripe")]
[EnableRateLimiting("webhook")]
public sealed class StripeWebhookController(
  IAppDbContext _db,
  IStripeSignatureValidator _signatureValidator,
  IWebhookProcessor _webhookProcessor,
  ILogger<StripeWebhookController> _logger) : ControllerBase
{
  [HttpPost("test")]
  public async Task<IActionResult> HandleTestWebhook()
  {
    return await HandleWebhookAsync("test");
  }

  [HttpPost("live")]
  public async Task<IActionResult> HandleLiveWebhook()
  {
    return await HandleWebhookAsync("live");
  }

  private async Task<IActionResult> HandleWebhookAsync(string mode)
  {
    // 1) Read raw body
    using var reader = new StreamReader(Request.Body);
    var payload = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(payload))
    {
      _logger.LogWarning("Empty webhook payload received");
      return BadRequest("Empty payload");
    }

    // 2) Get Stripe signature header
    if (!Request.Headers.TryGetValue("Stripe-Signature", out var signatureHeader) || string.IsNullOrWhiteSpace(signatureHeader))
    {
      _logger.LogWarning("Missing Stripe-Signature header");
      return BadRequest("Missing signature");
    }

    // 3) Parse event to get workspace ID (from metadata or known mapping)
    // MVP: We'll use a query parameter or extract from event metadata
    // For now, let's use query parameter: ?workspace_id=...
    if (!Request.Query.TryGetValue("workspace_id", out var workspaceIdStr) || !Guid.TryParse(workspaceIdStr, out var workspaceId))
    {
      _logger.LogWarning("Missing or invalid workspace_id query parameter");
      return BadRequest("Missing workspace_id");
    }

    // 4) Get webhook secret from ProviderConnection
    var connection = await _db.ProviderConnections
      .AsNoTracking()
      .FirstOrDefaultAsync(c => 
        c.WorkspaceId == workspaceId && 
        c.Provider == ProviderKind.Stripe && 
        c.Mode == (mode == "test" ? ProviderMode.Test :  ProviderMode.Live));

    if (connection == null)
    {
      _logger.LogWarning("No provider connection found for workspace {WorkspaceId} mode {Mode}", workspaceId, mode);
      return NotFound("Provider connection not found");
    }

    // 5) Verify signature
    if (!_signatureValidator.Validate(payload, signatureHeader!, connection.WebhookSecret))
    {
      _logger.LogWarning("Invalid Stripe signature for workspace {WorkspaceId}", workspaceId);
      return Unauthorized("Invalid signature");
    }

    // 6) Parse event
    var stripeEvent = Stripe.EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);

    string? ipCountryHint = null;
    if (Request.Headers.TryGetValue("CF-IPCountry", out var ipCountry) && !string.IsNullOrWhiteSpace(ipCountry))
    {
      ipCountryHint = ipCountry.ToString();
    }
    if (string.IsNullOrWhiteSpace(ipCountryHint) &&
      Request.Headers.TryGetValue("X-IP-Country", out var xip) && !string.IsNullOrWhiteSpace(xip))
    {
      ipCountryHint = xip.ToString();
    }
    // 7) Process webhook
    var command = new ProcessWebhookCommand(
      WorkspaceId: workspaceId,
      Provider: ProviderNames.Stripe,
      Mode: mode,
      EventId: stripeEvent.Id,
      EventType: stripeEvent.Type,
      CreatedUtc: stripeEvent.Created,
      PayloadJson: payload,
      IpCountryHint: ipCountryHint
    );

    var result = await _webhookProcessor.ProcessAsync(command);

    if (result.Success)
    {
      _logger.LogInformation("Successfully processed webhook {EventId} for workspace {WorkspaceId}", 
        stripeEvent.Id, workspaceId);
      return Ok(new { processed = true, eventId = stripeEvent.Id });
    }
    
    _logger.LogError("Failed to process webhook {EventId}: {Error}", stripeEvent.Id, result.ErrorMessage);

    if (result.Retryable)
    {
      _logger.LogWarning("Retryable error processing webhook {EventId}, Stripe will retry", stripeEvent.Id);
      return StatusCode(500, new { processed = false, error = result.ErrorMessage, retryable = true });
    }

    _logger.LogWarning("Non-retryable error processing webhook {EventId}, no retry needed", stripeEvent.Id);
    return Ok(new { processed = false, error = result.ErrorMessage, retryable = false });
  }
}
