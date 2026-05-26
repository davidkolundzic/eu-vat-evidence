using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatValidatorTests_HR
{
  [Theory]
  [InlineData("HR43118119983")]// Valid Croatian VAT number 
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
}
