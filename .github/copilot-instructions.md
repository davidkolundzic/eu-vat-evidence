# Copilot Instructions

## General Guidelines
- Korisnikov jezik je hrvatski - odgovaraj na hrvatskom jezik	u umjesto na engleskom.
- Komentari u kodu trebaju biti na internacionalnom engleskom jeziku.

You are helping develop "eu-vat-evidence", an open-source .NET 10 library for EU VAT number validation.

## Project structure
- VatEvidence.Core — NuGet package, no external dependencies
  - VatNumberValidator.cs — static Validate(string? rawVat) → VatValidationResult
  - CountryClassification.cs — EU/EEA/NonEU classification by ISO 3166-1 alpha-2
- VatEvidence.Vies — NuGet package, HTTP calls to EU VIES REST API
  - ViesClient.cs — IViesClient.CheckAsync(countryCode, vatNumber)
  - ViesResult.cs — IsActive, Name, Address, ErrorReason
- VatEvidence.Web — reference implementation (ASP.NET Core, PostgreSQL, EF Core snake_case)
- VatEvidence.Test.Unit — xUnit unit tests, no DB, no network

## Validation pattern
Each EU country has a private static method ValidateXX(string vat) where vat is already normalized (uppercase, no spaces/dashes).
Methods return VatValidationResult.Ok("XX", vat) or VatValidationResult.Fail("XX", "reason").
Checksum algorithms used: MOD-11-10 (ISO 7064) for HR, recursive MOD-11 for DE, MOD-97 for BE, MOD-89 for LU, MOD-11 for DK/FI/EL/SI/PL.

## Code style
- C# 13, .NET 10
- Records for result types
- Pattern matching in switch expressions
- No external NuGet dependencies in Core
- XML doc comments on all public members
- xUnit with [Theory] + [InlineData] for test cases

## Current focus
Implementing and testing EU VAT validators — format regex + checksum where applicable.
All 27 EU member states must be covered.