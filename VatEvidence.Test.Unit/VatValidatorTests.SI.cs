using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatValidatorTests_SI
{

  [Theory]
  [InlineData("SI50223054")]   // ✅ checksum verified (provjeri VIES)
  [InlineData("SI12345679")]   // ✅ checksum verified (sintetički)
  public void Validate_SI_Valid_ReturnsIsValid(string input)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.True(result.IsValid);
    Assert.Equal("SI", result.CountryCode);
    Assert.Null(result.ErrorReason);
  }

  [Theory]
  [InlineData("SI12345670", "SI VAT failed MOD-11 checksum.")]  // kriva kontrolna znamenka
  [InlineData("SI10000020", "SI VAT failed MOD-11 checksum.")]  // check==10, nema valjane znamenke
  [InlineData("SI1234567", "SI VAT must have exactly 8 digits (SI########).")]  // prekratko
  [InlineData("SI123456789", "SI VAT must have exactly 8 digits (SI########).")]  // predugo
  [InlineData("SI1234567A", "SI VAT must have exactly 8 digits (SI########).")]  // slovo umjesto znamenke
  public void Validate_SI_Invalid_ReturnsError(string input, string expectedReason)
  {
    var result = VatNumberValidator.Validate(input);

    Assert.False(result.IsValid);
    Assert.Equal("SI", result.CountryCode);
    Assert.Equal(expectedReason, result.ErrorReason);
  }
}
