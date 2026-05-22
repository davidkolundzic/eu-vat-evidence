using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace VatEvidence.Core.Validation;

/// <summary>
/// Local EU VAT number validator. No network calls, no external dependencies.
/// Validates format and checksum (where applicable) for all 27 EU member states.
/// </summary>
public class VatNumberValidator
{
  /// <summary>
  /// Validates an EU VAT number by format and checksum rules.
  /// </summary>
  /// <param name="rawVat">Raw input, e.g. "HR 12345678901" or "hr12345678901".</param>
  /// <returns>Validation result with country code and error reason if invalid.</returns>
  public static VatValidationResult Validate(string? rawVat)
  {
    if (string.IsNullOrWhiteSpace(rawVat))
      return VatValidationResult.Fail(null, "VAT number is empty.");

    var vat = rawVat.ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace(".", "");

    if (vat.Length < 4)
      return VatValidationResult.Fail(null, "VAT number is too short.");

    if (!Regex.IsMatch(vat[..2], @"^[A-Z]{2}$"))
      return VatValidationResult.Fail(null, "VAT number must start with a 2-letter country prefix.");

    var prefix = vat[..2];

    return prefix switch
    {
      "AT" => ValidateAT(vat),
      "BE" => ValidateBE(vat),
      "BG" => ValidateBG(vat),
      "CY" => ValidateCY(vat),
      "CZ" => ValidateCZ(vat),
      "DE" => ValidateDE(vat),
      "DK" => ValidateDK(vat),
      "EE" => ValidateEE(vat),
      "EL" => ValidateEL(vat),
      "ES" => ValidateES(vat),
      "FI" => ValidateFI(vat),
      "FR" => ValidateFR(vat),
      "HR" => ValidateHR(vat),
      "HU" => ValidateHU(vat),
      "IE" => ValidateIE(vat),
      "IT" => ValidateIT(vat),
      "LT" => ValidateLT(vat),
      "LU" => ValidateLU(vat),
      "LV" => ValidateLV(vat),
      "MT" => ValidateMT(vat),
      "NL" => ValidateNL(vat),
      "PL" => ValidatePL(vat),
      "PT" => ValidatePT(vat),
      "RO" => ValidateRO(vat),
      "SE" => ValidateSE(vat),
      "SI" => ValidateSI(vat),
      "SK" => ValidateSK(vat),
      _ => VatValidationResult.Fail(prefix, $"Unknown or non-EU VAT prefix: {prefix}")
    };
  }

  // -------------------------------------------------------------------------
  // EU member state validators
  // -------------------------------------------------------------------------

  // Austria: ATU######## (U + 8 digits, Luhn-style MOD-10)
  private static VatValidationResult ValidateAT(string vat)
  {
    if (!Regex.IsMatch(vat, @"^ATU\d{8}$"))
      return VatValidationResult.Fail("AT", "AT VAT must be ATU followed by 8 digits (ATU########).");

    string digits = vat[3..]; // skip "ATU", remaining 8 digits

    int c1 = (digits[1] - '0')
           + (digits[3] - '0')
           + (digits[5] - '0');

    int c2 = 0;
    foreach (int i in (int[])[0, 2, 4, 6])
    {
      int d = digits[i] - '0';
      int doubled = d * 2;
      c2 += doubled >= 10 ? doubled - 9 : doubled;
    }

    int expectedCheck = (10 - (c1 + c2) % 10) % 10;
    if (expectedCheck != (digits[7] - '0'))
      return VatValidationResult.Fail("AT", "AT VAT failed checksum.");

    return VatValidationResult.Ok("AT", vat);
  }

  // Belgium: BE0#########  (0 + 9 digits)
  private static VatValidationResult ValidateBE(string vat)
  {
    if (!Regex.IsMatch(vat, @"^BE0\d{9}$"))
      return VatValidationResult.Fail("BE", "BE VAT must be BE0 followed by 9 digits (BE0#########).");

    // MOD-97 checksum: last 2 digits = 97 - (first 8 digits MOD 97)
    var digits = vat[2..]; // 10 chars: 0 + 9 digits
    var number = long.Parse(digits[..8]);
    var check = int.Parse(digits[8..]);
    if (97 - (number % 97) != check)
      return VatValidationResult.Fail("BE", "BE VAT failed MOD-97 checksum.");
    return VatValidationResult.Ok("BE", vat);
  }

  // Bulgaria: BG######### or BG##########  (9 or 10 digits)
  private static VatValidationResult ValidateBG(string vat)
  {
    if (!Regex.IsMatch(vat, @"^BG\d{9,10}$"))
      return VatValidationResult.Fail("BG", "BG VAT must have 9 or 10 digits (BG#########).");
    return VatValidationResult.Ok("BG", vat);
  }

  // Cyprus: CY########L  (8 digits + 1 letter)
  private static VatValidationResult ValidateCY(string vat)
  {
    if (!Regex.IsMatch(vat, @"^CY\d{8}[A-Z]$"))
      return VatValidationResult.Fail("CY", "CY VAT must be 8 digits followed by a letter (CY########L).");
    return VatValidationResult.Ok("CY", vat);
  }

  // Czech Republic: CZ######## or CZ######### or CZ##########  (8–10 digits)
  private static VatValidationResult ValidateCZ(string vat)
  {
    if (!Regex.IsMatch(vat, @"^CZ\d{8,10}$"))
      return VatValidationResult.Fail("CZ", "CZ VAT must have 8 to 10 digits (CZ########).");
    return VatValidationResult.Ok("CZ", vat);
  }

  // Germany: DE######### (9 digits, recursive MOD-11)
  private static VatValidationResult ValidateDE(string vat)
  {
    if (!Regex.IsMatch(vat, @"^DE\d{9}$"))
      return VatValidationResult.Fail("DE", "DE VAT must have exactly 9 digits (DE#########).");

    string digits = vat[2..]; // "129274202" — all 9 digits

    if (digits[0] == '0')
      return VatValidationResult.Fail("DE", "DE VAT must not start with 0.");

    if (!CheckDeRecursiveMod11(digits))
      return VatValidationResult.Fail("DE", "DE VAT failed checksum.");

    return VatValidationResult.Ok("DE", vat);
  }

  

  // Denmark: DK########  (8 digits, MOD-11)
  private static VatValidationResult ValidateDK(string vat)
  {
    if (!Regex.IsMatch(vat, @"^DK\d{8}$"))
      return VatValidationResult.Fail("DK", "DK VAT must have exactly 8 digits (DK########).");

    int[] weights = [2, 7, 6, 5, 4, 3, 2, 1];
    var sum = vat[2..].Select((c, i) => (c - '0') * weights[i]).Sum();
    if (sum % 11 != 0)
      return VatValidationResult.Fail("DK", "DK VAT failed MOD-11 checksum.");

    return VatValidationResult.Ok("DK", vat);
  }

  // Estonia: EE#########  (9 digits)
  private static VatValidationResult ValidateEE(string vat)
  {
    if (!Regex.IsMatch(vat, @"^EE\d{9}$"))
      return VatValidationResult.Fail("EE", "EE VAT must have exactly 9 digits (EE#########).");
    return VatValidationResult.Ok("EE", vat);
  }

  // Greece: EL#########  (9 digits, MOD-11)
  private static VatValidationResult ValidateEL(string vat)
  {
    if (!Regex.IsMatch(vat, @"^EL\d{9}$"))
      return VatValidationResult.Fail("EL", "EL (Greece) VAT must have exactly 9 digits (EL#########).");

    int[] weights = [256, 128, 64, 32, 16, 8, 4, 2];
    var sum = vat[2..10].Select((c, i) => (c - '0') * weights[i]).Sum();
    var check = sum % 11 % 10;
    if (check != (vat[10] - '0'))
      return VatValidationResult.Fail("EL", "EL VAT failed MOD-11 checksum.");

    return VatValidationResult.Ok("EL", vat);
  }

  // Spain: ES[X]#######[X]  (letter or digit at start and end)
  private static VatValidationResult ValidateES(string vat)
  {
    if (!Regex.IsMatch(vat, @"^ES[A-Z0-9]\d{7}[A-Z0-9]$"))
      return VatValidationResult.Fail("ES", "ES VAT format: ES[X]#######[X] (ESA12345678 or ES12345678A).");
    return VatValidationResult.Ok("ES", vat);
  }

  // Finland: FI########  (8 digits, MOD-11)
  private static VatValidationResult ValidateFI(string vat)
  {
    if (!Regex.IsMatch(vat, @"^FI\d{8}$"))
      return VatValidationResult.Fail("FI", "FI VAT must have exactly 8 digits (FI########).");

    int[] weights = [7, 9, 10, 5, 8, 4, 2];
    var sum = vat[2..9].Select((c, i) => (c - '0') * weights[i]).Sum();
    var check = 11 - (sum % 11);
    var checkDigit = check == 11 ? 0 : check;
    if (checkDigit != (vat[9] - '0'))
      return VatValidationResult.Fail("FI", "FI VAT failed MOD-11 checksum.");

    return VatValidationResult.Ok("FI", vat);
  }

  // France: FR[XX]#########  (2 alphanum chars + 9 digits)
  private static VatValidationResult ValidateFR(string vat)
  {
    if (!Regex.IsMatch(vat, @"^FR[A-Z0-9]{2}\d{9}$"))
      return VatValidationResult.Fail("FR", "FR VAT format: FR[XX]######### (FRXX123456789).");
    return VatValidationResult.Ok("FR", vat);
  }

  // Croatia: HR###########  (11 digits, ISO 7064 MOD-11-10)
  private static VatValidationResult ValidateHR(string vat)
  {
    if (!Regex.IsMatch(vat, @"^HR\d{11}$"))
      return VatValidationResult.Fail("HR", "HR VAT must have exactly 11 digits (HR###########).");

    if (!CheckMod1110(vat[2..12], int.Parse(vat[12].ToString())))
      return VatValidationResult.Fail("HR", "HR VAT failed ISO 7064 MOD-11-10 checksum.");

    return VatValidationResult.Ok("HR", vat);
  }

  // Hungary: HU########  (8 digits)
  private static VatValidationResult ValidateHU(string vat)
  {
    if (!Regex.IsMatch(vat, @"^HU\d{8}$"))
      return VatValidationResult.Fail("HU", "HU VAT must have exactly 8 digits (HU########).");
    return VatValidationResult.Ok("HU", vat);
  }

  // Ireland: IE#[X]#####[L] or IE#[X]#####LL  (complex format)
  private static VatValidationResult ValidateIE(string vat)
  {
    // Old format: IE#A#####L, new format: IE########, IE#######LL
    if (!Regex.IsMatch(vat, @"^IE(\d{7}[A-Z]{1,2}|\d[A-Z+*]\d{5}[A-Z])$"))
      return VatValidationResult.Fail("IE", "IE VAT format: IE#######L or IE#######LL or IE#A#####L.");
    return VatValidationResult.Ok("IE", vat);
  }

  // Italy: IT###########  (11 digits)
  private static VatValidationResult ValidateIT(string vat)
  {
    if (!Regex.IsMatch(vat, @"^IT\d{11}$"))
      return VatValidationResult.Fail("IT", "IT VAT must have exactly 11 digits (IT###########).");

    // Luhn-style checksum
    var digits = vat[2..];
    var sum = 0;
    for (var i = 0; i < 10; i++)
    {
      var d = digits[i] - '0';
      if (i % 2 == 1)
      {
        d *= 2;
        if (d > 9) d -= 9;
      }
      sum += d;
    }
    var checkDigit = (10 - sum % 10) % 10;
    if (checkDigit != (digits[10] - '0'))
      return VatValidationResult.Fail("IT", "IT VAT failed checksum.");

    return VatValidationResult.Ok("IT", vat);
  }

  // Lithuania: LT#########  or LT############  (9 or 12 digits)
  private static VatValidationResult ValidateLT(string vat)
  {
    if (!Regex.IsMatch(vat, @"^LT(\d{9}|\d{12})$"))
      return VatValidationResult.Fail("LT", "LT VAT must have 9 or 12 digits (LT#########).");
    return VatValidationResult.Ok("LT", vat);
  }

  // Luxembourg: LU########  (8 digits)
  private static VatValidationResult ValidateLU(string vat)
  {
    if (!Regex.IsMatch(vat, @"^LU\d{8}$"))
      return VatValidationResult.Fail("LU", "LU VAT must have exactly 8 digits (LU########).");

    var number = int.Parse(vat[2..8]);
    var check = int.Parse(vat[8..]);
    if (number % 89 != check)
      return VatValidationResult.Fail("LU", "LU VAT failed MOD-89 checksum.");

    return VatValidationResult.Ok("LU", vat);
  }

  // Latvia: LV###########  (11 digits)
  private static VatValidationResult ValidateLV(string vat)
  {
    if (!Regex.IsMatch(vat, @"^LV\d{11}$"))
      return VatValidationResult.Fail("LV", "LV VAT must have exactly 11 digits (LV###########).");
    return VatValidationResult.Ok("LV", vat);
  }

  // Malta: MT########  (8 digits)
  private static VatValidationResult ValidateMT(string vat)
  {
    if (!Regex.IsMatch(vat, @"^MT\d{8}$"))
      return VatValidationResult.Fail("MT", "MT VAT must have exactly 8 digits (MT########).");
    return VatValidationResult.Ok("MT", vat);
  }

  // Netherlands: NL#########B##  (9 digits + B + 2 digits)
  private static VatValidationResult ValidateNL(string vat)
  {
    if (!Regex.IsMatch(vat, @"^NL\d{9}B\d{2}$"))
      return VatValidationResult.Fail("NL", "NL VAT format: NL#########B## (NL123456789B01).");
    return VatValidationResult.Ok("NL", vat);
  }

  // Poland: PL##########  (10 digits, weighted checksum)
  private static VatValidationResult ValidatePL(string vat)
  {
    if (!Regex.IsMatch(vat, @"^PL\d{10}$"))
      return VatValidationResult.Fail("PL", "PL VAT must have exactly 10 digits (PL##########).");

    int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
    var sum = vat[2..11].Select((c, i) => (c - '0') * weights[i]).Sum();
    var check = sum % 11;
    if (check == 10 || check != (vat[11] - '0'))
      return VatValidationResult.Fail("PL", "PL VAT failed checksum.");

    return VatValidationResult.Ok("PL", vat);
  }

  // Portugal: PT#########  (9 digits)
  private static VatValidationResult ValidatePT(string vat)
  {
    if (!Regex.IsMatch(vat, @"^PT\d{9}$"))
      return VatValidationResult.Fail("PT", "PT VAT must have exactly 9 digits (PT#########).");
    return VatValidationResult.Ok("PT", vat);
  }

  // Romania: RO##  to RO##########  (2–10 digits)
  private static VatValidationResult ValidateRO(string vat)
  {
    if (!Regex.IsMatch(vat, @"^RO\d{2,10}$"))
      return VatValidationResult.Fail("RO", "RO VAT must have 2 to 10 digits (RO##########).");
    return VatValidationResult.Ok("RO", vat);
  }

  // Sweden: SE############  (12 digits)
  private static VatValidationResult ValidateSE(string vat)
  {
    if (!Regex.IsMatch(vat, @"^SE\d{12}$"))
      return VatValidationResult.Fail("SE", "SE VAT must have exactly 12 digits (SE############).");
    return VatValidationResult.Ok("SE", vat);
  }

  // Slovenia: SI########  (8 digits, MOD-11)
  private static VatValidationResult ValidateSI(string vat)
  {
    if (!Regex.IsMatch(vat, @"^SI\d{8}$"))
      return VatValidationResult.Fail("SI", "SI VAT must have exactly 8 digits (SI########).");

    int[] weights = [8, 7, 6, 5, 4, 3, 2];
    var sum = vat[2..9].Select((c, i) => (c - '0') * weights[i]).Sum();
    var check = 11 - (sum % 11);
    if (check == 10 || check % 10 != (vat[9] - '0'))
      return VatValidationResult.Fail("SI", "SI VAT failed MOD-11 checksum.");

    return VatValidationResult.Ok("SI", vat);
  }

  // Slovakia: SK##########  (10 digits)
  private static VatValidationResult ValidateSK(string vat)
  {
    if (!Regex.IsMatch(vat, @"^SK\d{10}$"))
      return VatValidationResult.Fail("SK", "SK VAT must have exactly 10 digits (SK##########).");

    if (long.Parse(vat[2..]) % 11 != 0)
      return VatValidationResult.Fail("SK", "SK VAT failed MOD-11 checksum.");

    return VatValidationResult.Ok("SK", vat);
  }

  // -------------------------------------------------------------------------
  // Shared checksum helpers
  // -------------------------------------------------------------------------

  /// <summary>
  /// ISO 7064 MOD-11-10 algorithm. Used by HR and DE.
  /// Validates the check digit against the preceding digits.
  /// </summary>
  private static bool CheckMod1110(ReadOnlySpan<char> digits, int checkDigit)
  {
    var product = 10;
    foreach (var c in digits)
    {
      var sum = (product + (c - '0')) % 10;
      if (sum == 0) sum = 10;
      product = (sum * 2) % 11;
    }
    var expected = 11 - product;
    return expected == 10 ? checkDigit == 0 : checkDigit == expected;
  }
  private static bool CheckDeRecursiveMod11(string digits)
  {
    int p = 10;
    for (int i = 0; i < 8; i++)
    {
      int m = (digits[i] - '0' + p) % 10;
      if (m == 0) m = 10;
      p = 2 * m % 11;
    }

    int expected = 11 - p;
    if (expected == 10) expected = 0;

    return (digits[8] - '0') == expected;
  }
}

