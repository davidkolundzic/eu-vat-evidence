using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
//using System.Transactions;
using VatEvidence.Application.Evidence;
using VatEvidence.Domain;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Test.Integration.TestInfrastructure;
using VatEvidence.Test.Integration.TestInfrastructure.Builders;

namespace VatEvidence.Test.Integration.Evidence
{
  public sealed class EvidenceChainTests(LocalPostgresFixture postgresFixture) : IntegrationTestBase(postgresFixture), IClassFixture<LocalPostgresFixture>
  {     
    [Fact]
    public async Task AppendAsync_Twice_ShouldCreate_SequenceAndHashChain()
    {
      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var append = scope.ServiceProvider.GetRequiredService<IEvidenceAppendService>();

      var ws = WorkspaceBuilder.Default().Build();
      var tx = TransactionBuilder.Default()
        .ForWorkspaceId(ws.Id)
        .WithProviderTransactionId("pi_test_chain_1")
        .WithStatus(TransactionStatus.Insufficient)
        .Build();


      // 1) Write initial state to the database
      db.Workspaces.Add(ws);
      db.Transactions.Add(tx);
      await db.SaveChangesAsync();

      // Important: This test does not use a single database transaction to simulate a real scenario where two AppendAsync calls happen in different transactions (e.g. parallel webhooks).
      // - If both calls were in the same transaction, the second call would not see the evidence created by the first call because it wasn't committed yet, which would prevent testing proper chain formation.
      await using var dbTx = await db.Database.BeginTransactionAsync();


      // 2) Prvi evidence
      var ev1 = await append.AppendAsync(new AppendEvidenceCommand(
        TransactionId: tx.Id,
        EvidenceType: EvidenceType.Billingcountry,
        CountryCode: "DE",
        SourceRef: "evt_test_chain_1",
        CapturedUtc: DateTimeOffset.UtcNow
      ));

      // Persist the first evidence so it is visible to the next query
      await db.SaveChangesAsync();

      // 3) Second evidence
      var ev2 = await append.AppendAsync(new AppendEvidenceCommand(
        TransactionId: tx.Id,
        EvidenceType: EvidenceType.Ipcountry,
        CountryCode: "DE",
        SourceRef: "evt_test_chain_1",
        CapturedUtc: DateTimeOffset.UtcNow
      ));

      await db.SaveChangesAsync();
      await dbTx.CommitAsync();

      ev1.Sequence.Should().Be(1);
      ev2.Sequence.Should().Be(2);

      ev1.RecordHash.Should().NotBeNullOrWhiteSpace();
      ev2.RecordHash.Should().NotBeNullOrWhiteSpace();
      ev2.PrevRecordHash.Should().Be(ev1.RecordHash);
    }

    [Fact]
    public async Task AppendAsync_SameTypeSameSourceRef_ShouldBeIdempotent()
    {
      // This test simulates parallel webhooks attempting to write the same evidence (same tx, type and source_ref).
      using var scope = Factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var append = scope.ServiceProvider.GetRequiredService<IEvidenceAppendService>();

      var ws = WorkspaceBuilder.Default().Build();
      var tx = TransactionBuilder.Default()
        .ForWorkspaceId(ws.Id)
        .WithProviderTransactionId("pi_test_idempotent_1")
        .WithStatus(TransactionStatus.Insufficient)
        .Build();

      db.Workspaces.Add(ws);
      db.Transactions.Add(tx);
      await db.SaveChangesAsync();

      await using var dbTx = await db.Database.BeginTransactionAsync();

      var cmd = new AppendEvidenceCommand(
        TransactionId: tx.Id,
        EvidenceType: EvidenceType.Billingcountry,
        CountryCode: "DE",
        SourceRef: "evt_test_idempotent_1",
        CapturedUtc: DateTimeOffset.UtcNow
      );

      var e1 = await append.AppendAsync(cmd);
      var e2 = await append.AppendAsync(cmd);

      await db.SaveChangesAsync();
      await dbTx.CommitAsync();

      e2.Id.Should().Be(e1.Id); // The second call with the same type and source_ref should return the same evidence record, not create a new one. (AsNoTracing existing)

      var count = db.EvidenceRecords.Count(er =>
        er.TransactionId == tx.Id &&
        er.EvidenceType == EvidenceType.Billingcountry &&
        er.SourceRef == "evt_test_idempotent_1");

      count.Should().Be(1); // There should only be one record with that type and source_ref, confirming idempotency.
    }
  }
}
