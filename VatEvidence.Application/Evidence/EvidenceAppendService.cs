using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VatEvidence.Application.Interfaces;
using VatEvidence.Domain;

namespace VatEvidence.Application.Evidence
{
  public sealed class EvidenceAppendService(IAppDbContext _db, IEvidenceHashService _hash) : IEvidenceAppendService
  {
    public async Task<EvidenceRecord> AppendAsync(AppendEvidenceCommand command, CancellationToken ct = default)
    {
      // 1) Validacija (MVP minimalna)
      if (command.TransactionId == Guid.Empty)
      {
        throw new ArgumentException("TransactionId is required", nameof(command.TransactionId));
      }
      if (string.IsNullOrWhiteSpace(command.CountryCode) || command.CountryCode.Trim().Length != 2)
      {
        throw new ArgumentException("CountryCode must be  ISO2 (2 letters)", nameof(command.CountryCode));
      }
      if (string.IsNullOrWhiteSpace(command.SourceRef))
      {
        throw new ArgumentException("SourceRef is required", nameof(command.SourceRef));
      }

      var country = command.CountryCode!.Trim().ToUpperInvariant();
      var captured = command.CapturedUtc.ToUniversalTime();

      // 2) Lock transaction row sa FOR UPDATE (sprecava race condition kod paralelnih webhook-ova)
      // NAPOMENA: Caller (StripeWebhookProcessor) MORA imati aktivnu DB transakciju!
      var transaction = await _db.FromSqlInterpolated<Transaction>($@"
        SELECT *
        FROM transactions
        WHERE id = {command.TransactionId}
        FOR UPDATE
      ")
      .SingleOrDefaultAsync(ct);

      if (transaction is null)
      {
        throw new InvalidOperationException($"Transaction {command.TransactionId} not found");
      }

      // 3a) Idempotency u istom DbContext-u (pre SaveChangesAsync)
      var localExisting = _db.EvidenceRecords
        .Local
        .FirstOrDefault(x =>
          x.TransactionId == command.TransactionId &&
          x.EvidenceType == command.EvidenceType &&
          x.SourceRef == command.SourceRef!.Trim());

      if (localExisting is not null)
      {
        return localExisting;
      }

      // 3b) Idempotency u bazi (posle SaveChangesAsync)
      var existing = await _db.EvidenceRecords
        .AsNoTracking()
        .Where(x =>
          x.TransactionId == command.TransactionId &&
          x.EvidenceType == command.EvidenceType &&
          x.SourceRef == command.SourceRef!.Trim())
        .SingleOrDefaultAsync(ct);

      if (existing is not null)
      {
        return existing;
      }

      // 4) Tail lookup po SEQUENCE (determinističan ordering)
      var tail = await _db.EvidenceRecords
        .AsNoTracking()
        .Where(x => x.TransactionId == command.TransactionId)
        .OrderByDescending(x => x.Sequence)
        .Select(x => new { x.Sequence, x.RecordHash })
        .FirstOrDefaultAsync(ct);

      var nextSeq = (tail?.Sequence ?? 0) + 1;
      var tailHash = tail?.RecordHash;

      // 5) Kreiraj zapis
      var record = new EvidenceRecord
      {
        Id = Guid.NewGuid(),
        TransactionId = command.TransactionId,
        Sequence = nextSeq,
        CapturedUtc = captured,

        EvidenceType = command.EvidenceType,
        CountryCode = country,
        ValueRaw = command.ValueRaw,
        SourceRef = command.SourceRef!.Trim(),

        PrevRecordHash = string.IsNullOrWhiteSpace(tailHash) ? null : tailHash,
        RecordHash = string.Empty // postavi ispod
      };

      // 6) Izracunaj hash (sad kad imamo PrevRecordHash)
      record.RecordHash = _hash.ComputeRecordHash(record);

      // 7) Append (insert) u bazu
      // NAPOMENA: NE zovemo SaveChangesAsync - caller (StripeWebhookProcessor) commit-a cijelu transakciju
      _db.EvidenceRecords.Add(record);

      return record;
    }
  }
}