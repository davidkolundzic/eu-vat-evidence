using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using VatEvidence.Domain;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Test.Integration.TestInfrastructure;
using VatEvidence.Test.Integration.TestInfrastructure.Builders;
using VatEvidence.Test.Integration.TestInfrastructure.Helpers;

namespace VatEvidence.Test.Integration.Webhooks
{
  public sealed class StripeWebhooksTests : IntegrationTestBase, IClassFixture<LocalPostgresFixture>
  {
    public StripeWebhooksTests(LocalPostgresFixture postgresFixture) : base(postgresFixture)
    {
    }

    /// <summary>
    ///  --- Scenario: Webhook with invalid signature ---
    ///  - Given postoji ProviderConnection sa određenim webhook secret-om u bazi podataka  - (Setup) 
    /// </summary>
    /// <returns></returns>
    [Fact(DisplayName = "Webhook invalid signature → 401 i nema upisa")]
    public async Task Webhook_InvalidSignature_ShouldReturn401_AndNotPersistEvent()
    {
      const string secret = "whsec_test_secret_123";

      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var ws = WorkspaceBuilder.Default().Build();

      db.Workspaces.Add(ws);

      db.ProviderConnections.Add(new ProviderConnection
      {
        Id = Guid.NewGuid(),
        WorkspaceId = ws.Id,
        Provider = ProviderKind.Stripe,
        Mode = ProviderMode.Test,
        WebhookSecret = secret,
        CreatedAt = DateTimeOffset.UtcNow
      });



      await db.SaveChangesAsync();


      var payload = """
    {
      "id": "evt_test_001",
      "type": "payment_intent.succeeded",
      "created": 1700000000,
      "data": { "object": {
        "id": "pi_test_001",
        "amount": 2999,
        "currency": "eur",
        "created": 1700000000,
        "latest_charge": "ch_test_001",
        "receipt_email": "customer@example.com"
      } }
    }
    """;

      var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      var badHeader = $"t={timestamp},v1=deadbeef"; // namjerno krivo

      var req = new HttpRequestMessage(HttpMethod.Post,
  $"/api/webhooks/stripe/test?workspace_id={ws.Id}"); // ✅ Fixed: Use actual workspace ID
      req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
      req.Headers.TryAddWithoutValidation("Stripe-Signature", badHeader);

      var resp = await Client.SendAsync(req);

      resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

      using var scope2 = Factory.Services.CreateScope();
      var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
      (await db2.ProviderEvents.CountAsync()).Should().Be(0);
      (await db2.Transactions.CountAsync()).Should().Be(0);
      (await db2.EvidenceRecords.CountAsync()).Should().Be(0);
    }


    [Fact(DisplayName = "Webhook valid → 200 + upisi")]
    public async Task Webhook_ValidSignature_ShouldPersistEventTransactionAndEvidence()
    {
      const string secret = "whsec_test_secret_123";

      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var ws = WorkspaceBuilder.Default().Build();

      db.Workspaces.Add(ws);

      db.ProviderConnections.Add(new ProviderConnection
      {
        Id = Guid.NewGuid(),
        WorkspaceId = ws.Id,
        Provider = ProviderKind.Stripe,
        Mode = ProviderMode.Test,
        WebhookSecret = secret,
        CreatedAt = DateTimeOffset.UtcNow
      });

      await db.SaveChangesAsync();

      var payload = """{"id":"evt_test_002","object":"event","api_version":"2022-11-15","created":1700000001,"data":{"object":{"id":"pi_test_002","object":"payment_intent","amount":2999,"amount_capturable":0,"amount_received":2999,"application":null,"application_fee_amount":null,"canceled_at":null,"cancellation_reason":null,"capture_method":"automatic","client_secret":null,"confirmation_method":"automatic","created":1700000001,"currency":"eur","customer":null,"description":null,"invoice":null,"last_payment_error":null,"livemode":false,"metadata":{"ip_country":"HR"},"next_action":null,"on_behalf_of":null,"payment_method":null,"payment_method_options":{},"payment_method_types":["card"],"receipt_email":"customer@example.com","review":null,"setup_future_usage":null,"shipping":null,"source":null,"statement_descriptor":null,"statement_descriptor_suffix":null,"status":"succeeded","transfer_data":null,"transfer_group":null,"billing_details":{"address":{"country":"HR"}}}},"livemode":false,"pending_webhooks":1,"request":{"id":null,"idempotency_key":null},"type":"payment_intent.succeeded"}""";

      var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      var signature = StripeTestHelpers.CreateStripeSignatureHeader(payload, secret, timestamp);

      var req = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/webhooks/stripe/test?workspace_id={ws.Id}" // ✅ Koristi stvarni workspace ID
        );

      req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
      req.Headers.TryAddWithoutValidation("Stripe-Signature", signature);

      var resp = await Client.SendAsync(req);

      resp.StatusCode.Should().Be(HttpStatusCode.OK);

      using var scope2 = Factory.Services.CreateScope();
      var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

      (await db2.ProviderEvents.CountAsync()).Should().Be(1);
      (await db2.Transactions.CountAsync()).Should().Be(1);
      (await db2.EvidenceRecords.CountAsync()).Should().Be(2); // billing + ip - address evidence
    }

    [Fact(DisplayName = "Webhook idempotency: isti evt 2x → 200 ništa duplo")]
    public async Task Webhook_SameEventTwice_ShouldBeIdempotent()
    {
      const string secret = "whsec_test_secret_123";

      // seed workspace + provider connection(isto kao prethodna)
      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var ws = WorkspaceBuilder.Default().Build();

      db.Workspaces.Add(ws);
      db.ProviderConnections.Add(
        ProviderConnectionBuilder.Default()
        .WithWorkspaceId(ws.Id)
        .WithWebhookSecret(secret)
        .Build());

      await db.SaveChangesAsync();

      var payload = """
{
  "id": "evt_test_003",
  "object": "event",
  "api_version": "2022-11-15",
  "created": 1700000002,
  "data": {
    "object": {
      "id": "pi_test_003",
      "object": "payment_intent",
      "amount": 2999,
      "amount_capturable": 0,
      "amount_received": 2999,
      "created": 1700000002,
      "currency": "eur",
      "status": "succeeded",
      "receipt_email": "customer@example.com",
      "metadata": {
        "ip_country": "FR"
      },
      "billing_details": {
        "address": {
          "country": "FR",
          "city": "Paris",
          "line1": "Test Avenue 1",
          "postal_code": "75001"
        },
        "email": "customer@example.com",
        "name": "Test Customer"
      }
    }
  },
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": null,
    "idempotency_key": null
  },
  "type": "payment_intent.succeeded"
}
""";

      async Task<HttpStatusCode> SendOnce()
      {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = StripeTestHelpers.CreateStripeSignatureHeader(payload, secret, ts);

        var req = new HttpRequestMessage(HttpMethod.Post,
          $"/api/webhooks/stripe/test?workspace_id={ws.Id}"); // ✅ Koristi stvarni workspace ID
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Stripe-Signature", sig);

        var resp = await Client.SendAsync(req);
        return resp.StatusCode;
      }

      /// 1. Pošalji isti webhook 2x - (isti payload + signature)
      (await SendOnce()).Should().Be(HttpStatusCode.OK);
      (await SendOnce()).Should().Be(HttpStatusCode.OK);

      // 2. Provjeri u bazi da je event, transaction i evidence zapisan samo 1x
      using var scope2 = Factory.Services.CreateScope();
      var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

      (await db2.ProviderEvents.CountAsync()).Should().Be(1);
      (await db2.Transactions.CountAsync()).Should().Be(1);
      (await db2.EvidenceRecords.CountAsync()).Should().Be(2); // ne bi trebalo biti duplikata, dakle ostaje 2 kao u prethodnom testu a ne 4

    }
  }
}
