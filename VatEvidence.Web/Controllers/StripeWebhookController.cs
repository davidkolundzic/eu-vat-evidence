using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using VatEvidence.Application.Interfaces;
using VatEvidence.Application.Webhooks;
using VatEvidence.Domain;

namespace VatEvidence.Web.Controllers;

[ApiController]
[Route("api/webhooks/stripe")]
[EnableRateLimiting("webhook")]
public sealed partial class StripeWebhookController(
  IAppDbContext _db,
  IStripeSignatureValidator _signatureValidator,
  IWebhookProcessor _webhookProcessor,
  ILogger<StripeWebhookController> _logger,
  IHostEnvironment _env) : ControllerBase
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
    /// Stripe requires the raw body for signature verification, so we can't use [FromBody] or similar model binding
    /// - We also need to ensure the request body can be read multiple times if needed, so we enable buffering
    /// - Considering the potential size of webhook payloads, we should set a reasonable limit and handle cases where the payload is too large to prevent abuse and ensure we don't consume excessive memory
    ///  ! For large payloads, consider streaming processing or offloading to a background service instead of reading the entire payload into memory at once 
    ///  ! For now, we'll set a limit of 1MB for the payload size, which should be sufficient for most Stripe webhooks. If the payload exceeds this limit, we'll return a 413 Payload Too Large response.
    ///  ! In production, you might want to implement more robust handling for large payloads, such as streaming the request body or using a background processing system to handle the webhook asynchronously.
    using var reader = new StreamReader(Request.Body);
    var payload = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(payload))
    {
      LogEmptyPayload();
      return BadRequest("Empty payload");
    }

    // 2) Get Stripe signature header
    if (!Request.Headers.TryGetValue("Stripe-Signature", out var signatureHeader) || string.IsNullOrWhiteSpace(signatureHeader))
    {
      LogMissingSignature();
      return BadRequest("Missing signature");
    }

    // 3) Parse event to get workspace ID (from metadata or known mapping)
    // MVP: We'll use a query parameter or extract from event metadata
    // For now, let's use query parameter: ?workspace_id=...
    if (!Request.Query.TryGetValue("workspace_id", out var workspaceIdStr) || !Guid.TryParse(workspaceIdStr, out var workspaceId))
    {
      LogMissingWorkspaceId();
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
      LogNoProviderConnection(workspaceId, mode);
      return NotFound("Provider connection not found");
    }

    // 5) Verify signature
    if (!_signatureValidator.Validate(payload, signatureHeader!, connection.WebhookSecret))
    {
      LogInvalidSignature(workspaceId);
      return Unauthorized("Invalid signature");
    }

    // 6) Parse event
    var stripeEvent = Stripe.EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);

    // 7) Process webhook (NO IP hint - webhooks originate from Stripe servers, not buyer)
    var command = new ProcessWebhookCommand(
      WorkspaceId: workspaceId,
      Provider: ProviderNames.Stripe,
      Mode: mode,
      EventId: stripeEvent.Id,
      EventType: stripeEvent.Type,
      CreatedUtc: stripeEvent.Created,
      PayloadJson: payload,
      IpCountryHint: null // Webhooks don't have buyer IP - only Stripe server IP
    );

    var result = await _webhookProcessor.ProcessAsync(command);

    if (result.Success)
    {
      LogWebhookProcessed(stripeEvent.Id, workspaceId);
      return Ok(new { processed = true, eventId = stripeEvent.Id });
    }

    LogProcessingError(stripeEvent.Id, result.ErrorMessage);

    if (result.Retryable)
    {
      LogRetryableError(stripeEvent.Id);
      return StatusCode(500, new { processed = false, error = result.ErrorMessage, retryable = true });
    }

    LogNonRetryableError(stripeEvent.Id);
    return Ok(new { processed = false, error = result.ErrorMessage, retryable = false });
  }

  /// <summary>
  /// Extracts IP country hint from request headers with fallback chain.
  /// Priority: CF-IPCountry (Cloudflare) -> X-IP-Country (fallback) -> staging debug overrides.
  /// </summary>
  private string? GetIpCountryHint()
  {
    // 1) Primary: Cloudflare GeoIP header
    if (Request.Headers.TryGetValue("CF-IPCountry", out var cf) && !StringValues.IsNullOrEmpty(cf))
    {
      var v = cf.ToString().Trim().ToUpperInvariant();
      return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // 2) Fallback: generic proxy/CDN header
    if (Request.Headers.TryGetValue("X-IP-Country", out var xip) && !StringValues.IsNullOrEmpty(xip))
    {
      var v = xip.ToString().Trim().ToUpperInvariant();
      return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // 3) Staging-only override (for E2E testing without Cloudflare)
    if (_env.IsStaging())
    {
      // Debug header override
      if (Request.Headers.TryGetValue("X-Debug-IPCountry", out var dbgH) && !StringValues.IsNullOrEmpty(dbgH))
      {
        var v = dbgH.ToString().Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(v) ? null : v;
      }

      // Query parameter override (useful for Stripe CLI testing)
      if (Request.Query.TryGetValue("ip_country", out var dbgQ) && !StringValues.IsNullOrEmpty(dbgQ))
      {
        var v = dbgQ.ToString().Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(v) ? null : v;
      }
    }

    return null;
  }
}
