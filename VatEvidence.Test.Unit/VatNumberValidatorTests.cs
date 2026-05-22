using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatNumberValidatorTests
{
  [Theory]
  [InlineData("HR43118119983")]// Valid Croatian VAT number STAMBENI ZG d.o.o
  [InlineData("hr 43118119983")]
  public void ValidateVatNumber(string vat)
  {
    /// https://sudreg.pravosudje.hr/ords/r/esudreg/public/1?clear=APP
    /// https://www.fininfo.hr/
    var result = VatNumberValidator.Validate(vat);

    Assert.True(result.IsValid); // Should be valid - the format is correct and checksum should pass
    Assert.Equal("HR", result.CountryCode); // Should extract the correct country code
    Assert.Null(result.ErrorReason); // Should not have an error reason when valid
  }
  [Theory]
  [InlineData("DE123475223")]   // ✅ VIES verified 22/05/2026
  [InlineData("DE123456788")]   // ✅ checksum verified (sintetički)
  [InlineData("DE129274202")]   // ✅ checksum verified (sintetički)
  public void Validate_DE_Valid_ReturnsIsValid(string input)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.True(result.IsValid);
    Assert.Equal("DE", result.CountryCode);
    Assert.Null(result.ErrorReason);
  }

  [Theory]
  [InlineData("DE114103955", "DE VAT failed checksum.")]        // krivi checksum
  [InlineData("DE811193181", "DE VAT failed checksum.")]        // krivi checksum
  [InlineData("DE000000000", "DE VAT must not start with 0.")]  // počinje s 0
  [InlineData("DE12345678", "DE VAT must have exactly 9 digits (DE#########).")]
  [InlineData("DE1234567890", "DE VAT must have exactly 9 digits (DE#########).")]
  public void Validate_DE_Invalid_ReturnsError(string input, string expectedReason)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.False(result.IsValid);
    Assert.Equal("DE", result.CountryCode);
    Assert.Equal(expectedReason, result.ErrorReason);
  }

  // -- Invalid numbers --

  [Theory]
    [InlineData("HR12345678901")]   // pogrešan checksum
  [InlineData("HR123")]           // prekratak
  [InlineData("HR1234567890A")]   // slovo umjesto znamenke
  public void Validate_InvalidHR_ReturnsNotValid(string input)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.False(result.IsValid);
    Assert.Equal("HR", result.CountryCode);
    Assert.NotNull(result.ErrorReason);
  }

  // -- Edge cases --------------
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void Validate_NullOrEmpty_ReturnsNotValid(string? input)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.False(result.IsValid);
    Assert.Null(result.CountryCode);
  }

  [Fact]
  public void Validate_UnknownPrefix_ReturnsNotValid()
  {
    var result = VatNumberValidator.Validate("XX123456789");

    Assert.False(result.IsValid);
    Assert.Contains("XX", result.ErrorReason);
  }
  [Theory]
  [InlineData("ATU10223006")]   // ✅ checksum verified (sintetički)
  [InlineData("ATU12345674")]   // ✅ checksum verified (sintetički)
  public void Validate_AT_Valid_ReturnsIsValid(string input)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.True(result.IsValid);
    Assert.Equal("AT", result.CountryCode);
    Assert.Null(result.ErrorReason);
  }

  [Theory]
  [InlineData("ATU10223007", "AT VAT failed checksum.")]
  [InlineData("ATU99999999", "AT VAT failed checksum.")]
  [InlineData("AT10223006", "AT VAT must be ATU followed by 8 digits (ATU########).")]  // nedostaje U
  [InlineData("ATU1022300", "AT VAT must be ATU followed by 8 digits (ATU########).")]  // prekratko
  [InlineData("ATU102230060", "AT VAT must be ATU followed by 8 digits (ATU########).")]  // predugo
  public void Validate_AT_Invalid_ReturnsError(string input, string expectedReason)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.False(result.IsValid);
    Assert.Equal("AT", result.CountryCode);
    Assert.Equal(expectedReason, result.ErrorReason);
  }

}
