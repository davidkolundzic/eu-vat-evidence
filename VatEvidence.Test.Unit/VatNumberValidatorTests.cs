using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatNumberValidatorTests
{

  
  

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
  

}
