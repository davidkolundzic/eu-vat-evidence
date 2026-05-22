using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Core.Validation;

/// <summary>
/// Result of a local EU VAT number format validation.
/// Does not perform network calls — use VatEvidence.Vies for active status checks.
/// </summary>
public sealed record VatValidationResult
{
  /// <summary>Whether the VAT number passes format and checksum validation.</summary>
  public bool IsValid { get; init; }

  /// <summary>ISO 3166-1 alpha-2 country code extracted from the prefix (e.g. "HR", "DE").</summary>
  public string? CountryCode { get; init; }

  /// <summary>Normalised VAT number (uppercase, no spaces). Null when invalid.</summary>
  public string? NormalizedVat { get; init; }

  /// <summary>Human-readable reason for failure. Null when valid.</summary>
  public string? ErrorReason { get; init; }

  internal static VatValidationResult Ok(string countryCode, string normalized) =>
      new() { IsValid = true, CountryCode = countryCode, NormalizedVat = normalized };

  internal static VatValidationResult Fail(string? countryCode, string reason) =>
      new() { IsValid = false, CountryCode = countryCode, ErrorReason = reason };
}
