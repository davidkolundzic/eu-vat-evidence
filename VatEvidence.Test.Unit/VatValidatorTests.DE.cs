using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatValidatorTests_DE
{
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

}
