using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Stripe.Checkout;
using System.Text.Json;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Application.Options;
using VatEvidence.Domain;

namespace VatEvidence.Web.Controllers;

/// <summary>
/// Buyer-facing controller for creating Stripe Checkout Sessions.
/// CRITICAL: This is where IP evidence is captured from buyer's actual browser request (CF-IPCountry).
/// Webhooks originate from Stripe servers and must NOT create IP evidence.
/// </summary>
[ApiController]
[Route("api/stripe/checkout")]
public sealed partial class StripeCheckoutController(
  IAppDbContext _db,
  IEvidenceAppendService _evidenceAppendService,
  IOptions<StripeOptions> _stripeOptions,
  ILogger<StripeCheckoutController> _logger) : ControllerBase
{
  [HttpPost("session")]
  public async Task<IActionResult> CreateSession([FromBody] CreateCheckoutRequest request, CancellationToken ct = default)
  {
    if (request == null || request.WorkspaceId == Guid.Empty)
    {
      return BadRequest(new { error = "Invalid request" });
    }

    // 1) Extract buyer IP country from Cloudflare header
    var ipCountry = GetBuyerIpCountry();
    if (string.IsNullOrWhiteSpace(ipCountry))
    {
      LogMissingIpCountry();
      return BadRequest(new { error = "IP country not available (CF-IPCountry header missing)" });
    }

    LogIpCountryCaptured(ipCountry, request.WorkspaceId);

    // 2) Determine mode and API key
    var mode = string.Equals(request.Mode, "test", StringComparison.OrdinalIgnoreCase) ? "test" : "live";
    var apiKey = mode == "test" ? _stripeOptions.Value.TestSecretKey : _stripeOptions.Value.LiveSecretKey;

    if (string.IsNullOrWhiteSpace(apiKey))
    {
      return StatusCode(500, new { error = $"Stripe API key not configured for mode: {mode}" });
    }

    // 3) Create Stripe Checkout Session
    var sessionService = new SessionService();
    var requestOptions = new Stripe.RequestOptions { ApiKey = apiKey };

    Session session;
    try
    {
      var sessionCreateOptions = new SessionCreateOptions
      {
        Mode = "payment",
        SuccessUrl = request.SuccessUrl ?? "https://example.com/success?session_id={CHECKOUT_SESSION_ID}",
        CancelUrl = request.CancelUrl ?? "https://example.com/cancel",
        LineItems = new List<SessionLineItemOptions>
        {
          new SessionLineItemOptions
          {
            PriceData = new SessionLineItemPriceDataOptions
            {
              Currency = request.Currency ?? "eur",
              UnitAmount = request.AmountMinor > 0 ? request.AmountMinor : 1000, // Default 10.00 EUR
              ProductData = new SessionLineItemPriceDataProductDataOptions
              {
                Name = request.ProductName ?? "Test Product"
              }
            },
            Quantity = 1
          }
        },
        PaymentIntentData = new SessionPaymentIntentDataOptions
        {
          ReceiptEmail = request.CustomerEmail
        },
        CustomerEmail = request.CustomerEmail
      };

      session = await sessionService.CreateAsync(sessionCreateOptions, requestOptions, ct);
    }
    catch (Stripe.StripeException ex)
    {
      LogStripeError(ex, request.WorkspaceId);
      return StatusCode(500, new { error = "Failed to create Stripe Checkout session", details = ex.Message });
    }

    var paymentIntentId = session.PaymentIntentId;
    if (string.IsNullOrWhiteSpace(paymentIntentId))
    {
      LogMissingPaymentIntentId(session.Id);
      return StatusCode(500, new { error = "Stripe session missing PaymentIntentId" });
    }

    // 4) Upsert Transaction + Append IP evidence in DB transaction
    await using var dbTx = await _db.BeginTransactionAsync(ct);

    var txMode = mode == "test" ? ProviderMode.Test : ProviderMode.Live;

    try
    {
      // Upsert Transaction
      var transaction = await _db.Transactions
        .SingleOrDefaultAsync(x =>
          x.WorkspaceId == request.WorkspaceId &&
          x.Provider == ProviderKind.Stripe &&
          x.Mode == txMode &&
          x.ProviderTransactionId == paymentIntentId, ct);

      if (transaction is null)
      {
        transaction = new Transaction
        {
          Id = Guid.NewGuid(),
          WorkspaceId = request.WorkspaceId,
          Provider = ProviderKind.Stripe,
          Mode = txMode,
          ProviderTransactionId = paymentIntentId,
          ProviderChargeId = null, // Will be populated by webhook
          AmountMinor = request.AmountMinor,
          Currency = (request.Currency ?? "EUR").ToUpperInvariant(),
          CustomerEmail = request.CustomerEmail,
          CreatedUtc = DateTimeOffset.UtcNow,
          Status = TransactionStatus.Insufficient,
          StatusReason = "Awaiting payment"
        };

        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        LogTransactionCreated(transaction.Id, paymentIntentId, request.WorkspaceId);
      }

      // Append IP evidence
      var ipSnapshot = CreateIpSnapshot(ipCountry);

      await _evidenceAppendService.AppendAsync(
        new AppendEvidenceCommand(
          TransactionId: transaction.Id,
          EvidenceType: EvidenceType.Ipcountry,
          CountryCode: ipCountry,
          SourceRef: "cf-ipcountry",
          ValueRaw: ipSnapshot,
          CapturedUtc: DateTimeOffset.UtcNow
        ),
        ct);

      await _db.SaveChangesAsync(ct);
      await dbTx.CommitAsync(ct);

      LogIpEvidenceAppended(ipCountry, transaction.Id);

      return Ok(new
      {
        checkoutUrl = session.Url,
        sessionId = session.Id,
        paymentIntentId = paymentIntentId,
        transactionId = transaction.Id,
        ipCountry = ipCountry
      });
    }
    catch (Exception ex)
    {
      LogDatabaseError(ex, paymentIntentId, request.WorkspaceId);
      await dbTx.RollbackAsync(ct);
      return StatusCode(500, new { error = "Failed to store transaction/evidence", details = ex.Message });
    }
  }

  private string? GetBuyerIpCountry()
  {
    // Primary: Cloudflare GeoIP header
    if (Request.Headers.TryGetValue("CF-IPCountry", out var cf) && !StringValues.IsNullOrEmpty(cf))
    {
      var value = cf.ToString().Trim().ToUpperInvariant();
      if (IsValidCountryCode(value))
      {
        return value;
      }
    }

    return null;
  }

  private static bool IsValidCountryCode(string? code)
  {
    return code is { Length: 2 } && char.IsLetter(code[0]) && char.IsLetter(code[1]);
  }

  private static JsonDocument CreateIpSnapshot(string country)
  {
    var snapshot = new
    {
      country,
      source = "cf-ipcountry",
      headerPresent = true
    };

    var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
    return JsonDocument.Parse(json);
  }

  [LoggerMessage(Level = LogLevel.Warning, Message = "Missing CF-IPCountry header for checkout session creation")]
  private partial void LogMissingIpCountry();

  [LoggerMessage(Level = LogLevel.Information, Message = "IP country captured: {IpCountry} for workspace {WorkspaceId}")]
  private partial void LogIpCountryCaptured(string ipCountry, Guid workspaceId);

  [LoggerMessage(Level = LogLevel.Error, Message = "Stripe API error for workspace {WorkspaceId}")]
  private partial void LogStripeError(Exception ex, Guid workspaceId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe session {SessionId} missing PaymentIntentId")]
  private partial void LogMissingPaymentIntentId(string sessionId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Transaction {TransactionId} created for PI {PaymentIntentId}, workspace {WorkspaceId}")]
  private partial void LogTransactionCreated(Guid transactionId, string paymentIntentId, Guid workspaceId);

  [LoggerMessage(Level = LogLevel.Information, Message = "IP evidence appended: {IpCountry} for transaction {TransactionId}")]
  private partial void LogIpEvidenceAppended(string ipCountry, Guid transactionId);

  [LoggerMessage(Level = LogLevel.Error, Message = "Database error for PI {PaymentIntentId}, workspace {WorkspaceId}")]
  private partial void LogDatabaseError(Exception ex, string paymentIntentId, Guid workspaceId);
}

public sealed record CreateCheckoutRequest(
  Guid WorkspaceId,
  string Mode, // "test" or "live"
  long AmountMinor,
  string? Currency,
  string? ProductName,
  string? CustomerEmail,
  string? SuccessUrl,
  string? CancelUrl
);
