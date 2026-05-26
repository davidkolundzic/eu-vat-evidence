using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatValidatorTests_AT
{
  [Theory]
  [InlineData("ATU14194708")]   // ✅ VIES verified
  [InlineData("ATU33864707")]   // ✅ VIES verified (ne prati standardni checksum)
  [InlineData("ATU10223006")]   // ✅ format ok
  public void Validate_AT_Valid_ReturnsIsValid(string input)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.True(result.IsValid);
    Assert.Equal("AT", result.CountryCode);
    Assert.Null(result.ErrorReason);
  }

  [Theory]
  [InlineData("AT14194708", "AT VAT must be ATU followed by 8 digits (ATU########).")] // nedostaje U
  [InlineData("ATU1419470", "AT VAT must be ATU followed by 8 digits (ATU########).")] // prekratko
  [InlineData("ATU141947089", "AT VAT must be ATU followed by 8 digits (ATU########).")] // predugo
  [InlineData("ATU1419470X", "AT VAT must be ATU followed by 8 digits (ATU########).")] // slovo umjesto znamenke
  [InlineData("ATUX4194708", "AT VAT must be ATU followed by 8 digits (ATU########).")] // X umjesto prve znamenke
  public void Validate_AT_Invalid_ReturnsError(string input, string expectedReason)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.False(result.IsValid);
    Assert.Equal("AT", result.CountryCode);
    Assert.Equal(expectedReason, result.ErrorReason);
  }

}
