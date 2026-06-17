using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatValidatorTests_SI
{
    // ── Valid ─────────────────────────────────────────────────────────────────

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

    // ── Format errors ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SI1234567", "SI VAT must have exactly 8 digits (SI########).")]
    [InlineData("SI123456789", "SI VAT must have exactly 8 digits (SI########).")]
    [InlineData("SI1234567A", "SI VAT must have exactly 8 digits (SI########).")]
    [InlineData("SIA2345678", "SI VAT must have exactly 8 digits (SI########).")]
    public void Validate_SI_WrongFormat_ReturnsFormatError(string input, string expectedReason)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal("SI", result.CountryCode);
        Assert.Equal(expectedReason, result.ErrorReason);
    }

    // ── Checksum errors ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("SI12345670")] // kriva kontrolna znamenka
    [InlineData("SI10000020")] // check==10, nema valjane znamenke
    public void Validate_SI_WrongChecksum_ReturnsChecksumError(string input)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal("SI", result.CountryCode);
        Assert.Equal("SI VAT failed MOD-11 checksum.", result.ErrorReason);
    }

    // ── Whitespace / case tolerance ───────────────────────────────────────────

    [Theory]
    [InlineData("si50223054")]          // lowercase
    [InlineData("si 50223054")]         // lowercase + space
    [InlineData("SI 50223054")]         // uppercase + space
    [InlineData("SI-50223054")]         // dash separator
    public void Validate_SI_NormalizesInputBeforeValidation(string input)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.True(result.IsValid);
        Assert.Equal("SI", result.CountryCode);
        Assert.Equal("SI50223054", result.NormalizedVat);
    }
}
