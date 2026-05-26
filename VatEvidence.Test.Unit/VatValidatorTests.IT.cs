using VatEvidence.Core.Validation;

namespace VatEvidence.Test.Unit;

public class VatNumberValidatorTests_IT
{
	// ── Valid ─────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("IT00471330589")]   // ✅ Ferrero (javni podatak)
	[InlineData("IT12345678903")]   // ✅ checksum verified (sintetički)
	[InlineData("IT00000000000")]   // ✅ checksum verified (sintetički, rubni slučaj sve nule)
	[InlineData("IT01234567897")]   // ✅ checksum verified (sintetički)
	[InlineData("IT87654321097")]   // ✅ checksum verified (sintetički)
	[InlineData("IT11111111115")]   // ✅ checksum verified (sintetički)
	public void Validate_IT_Valid_ReturnsIsValid(string input)
	{
		var result = VatNumberValidator.Validate(input);

		Assert.True(result.IsValid);
		Assert.Equal("IT", result.CountryCode);
		Assert.NotNull(result.NormalizedVat);
		Assert.Null(result.ErrorReason);
	}

	// ── Format errors ─────────────────────────────────────────────────────────

	[Theory]
	[InlineData("IT1234567890", "IT VAT must have exactly 11 digits (IT###########).")] // 10 znamenki
	[InlineData("IT123456789012", "IT VAT must have exactly 11 digits (IT###########).")] // 12 znamenki
	[InlineData("IT1234567890A", "IT VAT must have exactly 11 digits (IT###########).")] // slovo na kraju
	[InlineData("ITA2345678903", "IT VAT must have exactly 11 digits (IT###########).")] // slovo na početku
	public void Validate_IT_WrongFormat_ReturnsFormatError(string input, string expectedReason)
	{
		var result = VatNumberValidator.Validate(input);

		Assert.False(result.IsValid);
		Assert.Equal("IT", result.CountryCode);
		Assert.Equal(expectedReason, result.ErrorReason);
	}

	// ── Checksum errors ───────────────────────────────────────────────────────

	[Theory]
	[InlineData("IT12345678900")] // check mora biti 3, nije 0
	[InlineData("IT12345678901")] // check mora biti 3, nije 1
	[InlineData("IT12345678902")] // check mora biti 3, nije 2
	[InlineData("IT00000000001")] // check mora biti 0 (sve nule), nije 1
	[InlineData("IT11111111110")] // check mora biti 5, nije 0
	public void Validate_IT_WrongChecksum_ReturnsChecksumError(string input)
	{
		var result = VatNumberValidator.Validate(input);

		Assert.False(result.IsValid);
		Assert.Equal("IT", result.CountryCode);
		Assert.Equal("IT VAT failed checksum.", result.ErrorReason);
	}

	// ── Whitespace / case tolerance ───────────────────────────────────────────

	[Theory]
	[InlineData("it12345678903")]          // lowercase
	[InlineData("it 12345678903")]         // lowercase + space
	[InlineData("IT 12345678903")]         // uppercase + space
	[InlineData("IT-12345678903")]         // dash separator
	public void Validate_IT_NormalizesInputBeforeValidation(string input)
	{
		var result = VatNumberValidator.Validate(input);

		Assert.True(result.IsValid);
		Assert.Equal("IT", result.CountryCode);
		Assert.Equal("IT12345678903", result.NormalizedVat);
	}
}