using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using VatEvidence.Domain;

namespace VatEvidence.Application.Evidence
{
  public sealed record AppendEvidenceCommand(
    Guid TransactionId,
    EvidenceType EvidenceType,
    string? CountryCode,
    string? SourceRef,
    DateTimeOffset CapturedUtc,
    JsonDocument? ValueRaw = null
  );
  
}
