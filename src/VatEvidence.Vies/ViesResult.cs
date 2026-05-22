namespace VatEvidence.Vies;

/// <summary>
/// Result of an EU VIES active-status check.
/// Intentionally decoupled from VatEvidence.Core — this package has zero dependencies.
/// </summary>
public sealed record ViesResult
{
  /// <summary>Whether VIES confirmed the VAT number is active.</summary>
  public bool IsActive { get; init; }

  /// <summary>ISO 3166-1 alpha-2 country code queried.</summary>
  public string CountryCode { get; init; } = default!;

  /// <summary>VAT number queried (without country prefix).</summary>
  public string VatNumber { get; init; } = default!;

  /// <summary>Registered company name returned by VIES. May be null if not disclosed.</summary>
  public string? Name { get; init; }

  /// <summary>Registered company address returned by VIES. May be null if not disclosed.</summary>
  public string? Address { get; init; }

  /// <summary>Date of the VIES request.</summary>
  public DateOnly RequestDate { get; init; }

  /// <summary>Reason for failure when IsActive is false. Null on success.</summary>
  public string? ErrorReason { get; init; }

  internal static ViesResult Active(string countryCode, string vatNumber,
      string? name, string? address, DateOnly requestDate) => new()
      {
        IsActive = true,
        CountryCode = countryCode,
        VatNumber = vatNumber,
        Name = name,
        Address = address,
        RequestDate = requestDate
      };

  internal static ViesResult Inactive(string countryCode, string vatNumber,
      DateOnly requestDate) => new()
      {
        IsActive = false,
        CountryCode = countryCode,
        VatNumber = vatNumber,
        RequestDate = requestDate,
        ErrorReason = "VAT number is not active in VIES."
      };

  internal static ViesResult Error(string countryCode, string vatNumber, string reason) => new()
  {
    IsActive = false,
    CountryCode = countryCode,
    VatNumber = vatNumber,
    RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
    ErrorReason = reason
  };
}