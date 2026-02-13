using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VatEvidence.Application.Crypto;
using VatEvidence.Domain;

namespace VatEvidence.Application.Evidence
{
  public sealed class EvidenceHashService : IEvidenceHashService
  {
    public string ComputeRecordHash(EvidenceRecord r)
    {
      var capturedUtc = r.CapturedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
      var valueRaw = Hashing.CanonicalJsonOrEmpty(r.ValueRaw);
      var prev = r.PrevRecordHash ?? "";

      // Deterministički format (ne mijenjati kasnije bez versioning-a)
      var sb = new StringBuilder(512);
      sb.Append("v1|");
      sb.Append(r.TransactionId).Append('|');
      sb.Append(capturedUtc).Append('|');
      sb.Append((int)r.EvidenceType).Append('|');
      sb.Append((r.CountryCode ?? "").Trim().ToUpperInvariant()).Append('|');
      sb.Append((r.SourceRef ?? "").Trim()).Append('|');
      sb.Append(valueRaw).Append('|');
      sb.Append(prev);

      return Hashing.Sha256Hex(sb.ToString());
    }
  }
}
