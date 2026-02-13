namespace VatEvidence.Application.Evidence
{
  public interface IEvidenceChainVerifier
  {
    Task<EvidenceChainVerifyResult> VerifyAsync(Guid transactionId, CancellationToken ct = default);
  }
}
