using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text.Json;
using VatEvidence.Domain;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Test.Integration.TestInfrastructure;

namespace VatEvidence.Test.Integration.Smoke
{
  public sealed class DbResetSmokeTests : IntegrationTestBase, IClassFixture<LocalPostgresFixture>
  {
    public DbResetSmokeTests(LocalPostgresFixture postgresFixture) : base(postgresFixture) { }

    [Fact]
    public async Task DbReset_Should_Truncate_All_Tables()
    {
      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

      var transaction = new Transaction
      {
        Id = TestGuids.TransactionId,
        Provider = ProviderKind.Stripe,
        Mode = ProviderMode.Test,
        ProviderTransactionId = "pi_reset_test",
        AmountMinor = 100,
        Currency = CurrencyCodes.EUR,
        CreatedUtc = DateTimeOffset.UtcNow
      };
      db.Transactions.Add(transaction);

      var providerEvent = new ProviderEvent
      {
        Id = TestGuids.ProviderEventId,
        Provider = ProviderKind.Stripe,
        Mode = ProviderMode.Test,
        ProviderEventId = "evt_reset_test",
        Type = "payment_intent.succeeded",
        CreatedUtc = DateTimeOffset.UtcNow,
        ReceivedUtc = DateTimeOffset.UtcNow,
        PayloadJson = JsonDocument.Parse("{}"),
        PayloadHash = "hash-reset-test",
        ProcessingStatus = EventProcessingStatus.Processed
      };
      db.ProviderEvents.Add(providerEvent);

      await db.SaveChangesAsync();

      (await db.Transactions.CountAsync()).Should().BeGreaterThan(0);
      (await db.ProviderEvents.CountAsync()).Should().BeGreaterThan(0);

      await DbReset.Reset(Factory.Services);

      var scope2 = Factory.Services.CreateScope();
      var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
      (await db2.Transactions.CountAsync()).Should().Be(0);
      (await db2.ProviderEvents.CountAsync()).Should().Be(0);
      (await db2.EvidenceRecords.CountAsync()).Should().Be(0);
    }
  }
}