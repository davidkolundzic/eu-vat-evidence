using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Core.Classification;

/// <summary>Country classification for EU, EEA and non-EU countries.</summary>
public enum CountryGroup
{
  /// <summary>
  /// European Union (EU) member states, which are subject to EU VAT rules and require VAT validation for intra-EU transactions.
  /// </summary>
  EU,
  /// <summary>
  /// European Economic Area (EEA) member states, which are not in the EU but have agreements for VAT and other economic cooperation.
  /// </summary>
  EEA,
  /// <summary>
  /// Non-EU countries, which are not subject to EU VAT rules.
  /// </summary>
  NonEU
}

/// <summary>
/// Classifies countries by their EU/EEA membership using ISO 3166-1 alpha-2 codes.
/// </summary>
public static class CountryClassification
{
  private static readonly HashSet<string> _euCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES",
        "FI", "FR", "HR", "HU", "IE", "IT", "LT", "LU", "LV", "MT",
        "NL", "PL", "PT", "RO", "SE", "SI", "SK"
    };

  private static readonly HashSet<string> _eeaCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "IS", "LI", "NO"
    };

  /// <summary>Returns the group classification for a given ISO 3166-1 alpha-2 country code.</summary>
  public static CountryGroup Classify(string? isoCode)
  {
    if (string.IsNullOrWhiteSpace(isoCode))
      return CountryGroup.NonEU;

    if (_euCountries.Contains(isoCode)) return CountryGroup.EU;
    if (_eeaCountries.Contains(isoCode)) return CountryGroup.EEA;
    return CountryGroup.NonEU;
  }

  /// <summary>Returns true if the country is an EU member state.</summary>
  public static bool IsEU(string? isoCode) => Classify(isoCode) == CountryGroup.EU;

  /// <summary>Returns true if the country requires EU VAT validation.</summary>
  public static bool RequiresVatValidation(string? isoCode) => IsEU(isoCode);

  /// <summary>All EU VAT prefixes (note: Greece uses EL, not GR).</summary>
  public static IReadOnlySet<string> AllEuVatPrefixes => _euCountries;
}