using Microsoft.EntityFrameworkCore;
using VatEvidence.Application.Interfaces;
using VatEvidence.Domain;

namespace VatEvidence.Application.Evidence
{
  public sealed class EvidenceChainVerifier(IAppDbContext _db, IEvidenceHashService _hash) : IEvidenceChainVerifier
  {
    public async Task<EvidenceChainVerifyResult> VerifyAsync(Guid transactionId, CancellationToken ct = default)
    {
      var records = await _db.EvidenceRecords
          .AsNoTracking()
          .Where(x => x.TransactionId == transactionId)
          .OrderBy(x => x.Sequence)
          .ToListAsync(ct);

      var issues = new List<EvidenceChainIssue>();

      string? expectedPrev = null;
      for (int i = 0; i < records.Count; i++)
      {
        var r = records[i];

        var actualPrev = r.PrevRecordHash;
        var expectedPrevForThis = expectedPrev;

        if ((expectedPrevForThis ?? "") != (actualPrev ?? ""))
        {
          issues.Add(new EvidenceChainIssue(
              r.Id,
              "PrevMismatch",
              expectedPrevForThis ?? "",
              actualPrev ?? ""
          ));
        }

        var expectedHash = _hash.ComputeRecordHash(r);
        var actualHash = r.RecordHash ?? "";
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
          issues.Add(new EvidenceChainIssue(
              r.Id,
              "HashMismatch",
              expectedHash,
              actualHash
          ));
        }

        expectedPrev = actualHash;
      }

      var isValid = issues.Count == 0;
      return new EvidenceChainVerifyResult(
          TransactionId: transactionId,
          IsValid: isValid,
          RecordsChecked: records.Count,
          HeadHash: records.Count > 0 ? records[0].RecordHash : null,
          TailHash: records.Count > 0 ? records[^1].RecordHash : null,
          Issues: issues
      );
    }
  }
}

