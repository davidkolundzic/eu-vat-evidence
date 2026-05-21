# eu-vat-evidence

Open source .NET library for EU VAT compliance evidence — country classification, VAT number validation, and tamper-evident hash-chain audit records for digital services.

## Overview

When selling digital services to EU customers, businesses must collect and retain evidence of the buyer's location to apply the correct VAT rate. This project provides the core building blocks to do that correctly.

**What this library does:**

- Validates EU VAT numbers (format + checksum per country)
- Classifies countries as EU / EEA / non-EU (ISO 3166-1 alpha-2)
- Builds a tamper-evident hash-chain of evidence records per transaction
- Verifies chain integrity (detects tampering or missing records)
- Generates realistic test data for development and integration testing

## Status

> 🚧 **Active development.** Core evidence chain and country classification are stable. VAT number validator is the current focus.

## Roadmap

- [x] Country classification (EU / EEA / non-EU)
- [x] Evidence hash-chain (append + verify)
- [x] PostgreSQL persistence via EF Core
- [x] Integration test infrastructure
- [ ] **EU VAT number validator** ← current focus
- [ ] Test data generator (fake transactions, VAT numbers, IP addresses)
- [ ] REST API for evidence submission and chain verification
- [ ] Export (CSV / PDF audit report)

## Getting Started

### Prerequisites

- .NET 10
- PostgreSQL 15+

### Run locally

```bash
git clone https://github.com/your-username/eu-vat-evidence.git
cd eu-vat-evidence

# Set connection string
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=VatEvidence;Username=postgres;Password=postgres"

dotnet restore
dotnet ef database update --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
dotnet run --project VatEvidence.Web
```

### Run tests

```bash
dotnet test
```

Tests use [Testcontainers](https://testcontainers.com/) — Docker must be running.

## Project Structure

```
VatEvidence.Domain/           # Entities, enums, CountryClassification
VatEvidence.Application/      # Evidence hash-chain, append & verify services
VatEvidence.Infrastructure/   # EF Core, PostgreSQL, migrations
VatEvidence.Web/              # ASP.NET Core API
VatEvidence.Test.Integration/ # Integration tests (xUnit + Testcontainers)
```

## Key Concepts

### Country classification

```csharp
var ctx = CountryClassification.Classify("HR");

ctx.IsValid  // true
ctx.IsEu     // true
ctx.IsEea    // true
ctx.Code     // "HR"
```

### Evidence hash-chain

Each evidence record is cryptographically linked to the previous one, forming a tamper-evident chain per transaction.

```
Record 1: hash = SHA256("v1|tx_id|timestamp|type|country|source||")
Record 2: hash = SHA256("v1|tx_id|timestamp|type|country|source||record1_hash")
Record 3: hash = SHA256("v1|tx_id|timestamp|type|country|source||record2_hash")
```

Verification checks that every hash and link in the chain is intact.

### VAT number validator *(in progress)*

```csharp
// Coming soon
var result = VatNumberValidator.Validate("HR12345678901");

result.IsValid      // true / false
result.CountryCode  // "HR"
result.ErrorReason  // null or description of failure
```

Each EU member state has its own format rules and checksum algorithm. The validator will support all 27 EU member states.

## Tech Stack

- **Language:** C# / .NET 10
- **Database:** PostgreSQL (EF Core + snake_case conventions)
- **Testing:** xUnit, FluentAssertions, Testcontainers
- **Hashing:** SHA-256

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md).

Good first issues:
- Add VAT number validation for a specific EU country
- Add test cases for edge-case country codes
- Improve documentation

## License

[MIT](LICENSE)