using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using VatEvidence.Domain;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Test.Integration.TestInfrastructure;

namespace VatEvidence.Test.Integration.Smoke
{
  public sealed class DbResetSmokeTests : IntegrationTestBase, IClassFixture<LocalPostgresFixture>
  {
    public DbResetSmokeTests(LocalPostgresFixture postgresFixture): base(postgresFixture) {}


    /// <summary>
    ///  - Testira da li DbReset.Reset() funkcija ispravno truncira sve tabele u bazi.
    ///  - Prvo ubacuje test podatke u ključne tablice (Workspaces, Transactions, ProviderEvents).
    ///  - Zatim poziva DbReset.Reset() i provjerava da su tabele prazne.
    ///  - Ako nema izuzetaka i ako su tabele prazne nakon resetiranja, test se smatra uspješnim.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task DbReset_Should_Truncate_All_Tables()
    {
      // Arrange
      // ubaci barem po 1 u par kljucnih tablica

            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Ubaci test podatke
            var workspace = new Workspace
            {
                Id = TestGuids.WorkspaceId,
                Name = "WS",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Workspaces.Add(workspace);


            var transaction = new Transaction
            {
                Id = TestGuids.TransactionId,
                WorkspaceId = workspace.Id,
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
                WorkspaceId = workspace.Id,
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

      // sanity check da su podaci ubačeni
      (await db.Workspaces.CountAsync()).Should().BeGreaterThan(0);
      (await db.Transactions.CountAsync()).Should().BeGreaterThan(0);
      (await db.ProviderEvents.CountAsync()).Should().BeGreaterThan(0);

      // Act: resetiraj bazu
      await DbReset.Reset(Factory.Services);

      // Assert
      // Ako nema izuzetaka, test je prošao
      var scope2 = Factory.Services.CreateScope();
      var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
      (await db2.Workspaces.CountAsync()).Should().Be(0);
      (await db2.Transactions.CountAsync()).Should().Be(0);
      (await db2.ProviderEvents.CountAsync()).Should().Be(0);
      (await db2.EvidenceRecords.CountAsync()).Should().Be(0);

    }
  }
}
