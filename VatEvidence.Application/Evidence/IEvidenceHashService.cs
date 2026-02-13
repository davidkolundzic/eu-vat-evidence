using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Application.Evidence
{
  public interface IEvidenceHashService
  {
    string ComputeRecordHash(EvidenceRecord r);
  }
}

