using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatValidatorTests_SK
{
    // ── Valid ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SK2021234567")]   // ✅ synthetic, 10 digits after SK
    [InlineData("SK0000000000")]   // ✅ synthetic (edge case all zeros)
    public void Validate_SK_Valid_ReturnsIsValid(string input)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.True(result.IsValid);
        Assert.Equal("SK", result.CountryCode);
        Assert.Null(result.ErrorReason);
    }

    // ── Format errors ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SK123456789", "SK VAT must have exactly 10 digits (SK##########).")]
    [InlineData("SK12345678901", "SK VAT must have exactly 10 digits (SK##########).")]
    [InlineData("SK12345678A", "SK VAT must have exactly 10 digits (SK##########).")]
    [InlineData("SKA234567890", "SK VAT must have exactly 10 digits (SK##########).")]
    public void Validate_SK_WrongFormat_ReturnsFormatError(string input, string expectedReason)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal("SK", result.CountryCode);
        Assert.Equal(expectedReason, result.ErrorReason);
    }

    // ── Checksum errors ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("SK1234567891")] // invalid checksum
    [InlineData("SK0000000001")] // invalid checksum
    public void Validate_SK_WrongChecksum_ReturnsChecksumError(string input)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal("SK", result.CountryCode);
        Assert.Equal("SK VAT failed MOD-11 checksum.", result.ErrorReason);
    }

    // ── Whitespace / case tolerance ───────────────────────────────────────────

    [Theory]
    [InlineData("sk2021234567")]          // lowercase
    [InlineData("sk 2021234567")]         // lowercase + space
    [InlineData("SK 2021234567")]         // uppercase + space
    [InlineData("SK-2021234567")]         // dash separator
    public void Validate_SK_NormalizesInputBeforeValidation(string input)
    {
        var result = VatNumberValidator.Validate(input);

        Assert.True(result.IsValid);
        Assert.Equal("SK", result.CountryCode);
        Assert.Equal("SK2021234567", result.NormalizedVat);
    }
}
