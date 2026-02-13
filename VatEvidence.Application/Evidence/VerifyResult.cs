using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Application.Evidence
{
  /// <summary>
  /// Verify result model
  /// </summary>
  /// <param name="EvidenceId"></param>
  /// <param name="Kind"></param>
  /// <param name="Excpected"></param>
  /// <param name="Actual"></param>
  public sealed record EvidenceChainIssue
  (
    Guid EvidenceId,
      string Kind, // "PrevMismatch" | "HashMismatch" | "OutOfOrder"
      string Expected,
      string Actual
  );

  /// <summary>
  /// Represents the result of verifying an evidence chain for a specific transaction, including validation status,
  /// record count, hash information, and any issues found.
  /// </summary>
  /// <remarks>Use this result to assess the integrity of an evidence chain and to review any specific issues
  /// that may have affected its validity. The hash values can be used for further integrity checks or auditing
  /// purposes.</remarks>
  /// <param name="TransactionId">The unique identifier of the transaction whose evidence chain was verified.</param>
  /// <param name="IsValid">A value indicating whether the evidence chain is valid. Set to <see langword="true"/> if the chain passed all
  /// verification checks; otherwise, <see langword="false"/>.</param>
  /// <param name="RecordsChecked">The total number of records that were checked during the verification process.</param>
  /// <param name="HeadHash">The hash value of the first record in the evidence chain, or <see langword="null"/> if the chain is empty.</param>
  /// <param name="TailHash">The hash value of the last record in the evidence chain, or <see langword="null"/> if the chain is empty.</param>
  /// <param name="Issues">A read-only list of issues identified during the verification process. The list is empty if no issues were found.</param>
  public sealed record EvidenceChainVerifyResult(
    Guid TransactionId,
    bool IsValid,
    int RecordsChecked,
    string? HeadHash,
    string? TailHash,
    IReadOnlyList<EvidenceChainIssue> Issues
  );
}
