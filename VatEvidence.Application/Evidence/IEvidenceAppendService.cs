using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Application.Evidence
{
  public interface IEvidenceAppendService
  {
    Task<EvidenceRecord> AppendAsync(AppendEvidenceCommand command, CancellationToken ct=default );
  }
}
