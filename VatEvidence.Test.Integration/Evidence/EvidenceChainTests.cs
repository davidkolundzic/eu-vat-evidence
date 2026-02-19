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


      // 1) Upiši početno stanje u bazu
      db.Workspaces.Add(ws);
      db.Transactions.Add(tx);
      await db.SaveChangesAsync();

      // Važno: Ovaj test ne koristi transakciju baze da bi simulirao realan scenarij gdje se dva poziva AppendAsync dešavaju u različitim transakcijama (npr. paralelni webhook-ovi).
      // - Ako bi se oba poziva dešavala unutar iste transakcije, drugi poziv ne bi vidio evidencu kreiranu prvim pozivom jer nije bila committana, što bi onemogućilo testiranje ispravnog formiranja lanca.
      await using var dbTx = await db.Database.BeginTransactionAsync();


      // 2) Prvi evidence
      var ev1 = await append.AppendAsync(new AppendEvidenceCommand(
        TransactionId: tx.Id,
        EvidenceType: EvidenceType.Billingcountry,
        CountryCode: "DE",
        SourceRef: "evt_test_chain_1",
        CapturedUtc: DateTimeOffset.UtcNow
      ));

      // Snimi prvi evidence da bude vidljiv sledećem upitu
      await db.SaveChangesAsync();

      // 3) Drugi evidence
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
      // Ovaj test simulira scenarij paralelnih webhook-ova koji pokušavaju da upišu isti evidence (isti tx, type i source_ref).
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

      e2.Id.Should().Be(e1.Id); // Drugi poziv sa istim type i source_ref treba da vrati isti evidence record, a ne da kreira novi. (AsNoTracing existing)

      var count = db.EvidenceRecords.Count(er =>
        er.TransactionId == tx.Id &&
        er.EvidenceType == EvidenceType.Billingcountry &&
        er.SourceRef == "evt_test_idempotent_1");

      count.Should().Be(1); // Treba postojati samo jedan record sa tim type i source_ref, što potvrđuje idempotentnost.
    }
  }
}
