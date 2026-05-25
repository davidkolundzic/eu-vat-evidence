# eu-vat-evidence

**Open-source .NET library for EU VAT number validation and compliance evidence.**

Built for developers who need reliable, auditable VAT handling in European B2B applications — without pulling in heavy dependencies or cloud services for basic format checks.

---

## Why this exists

EU VAT compliance is a solved problem in theory and a surprisingly fragmented one in practice. Most libraries either:

- validate format only, with no path to active-status verification
- call VIES directly without local pre-validation, wasting network round-trips on obviously invalid numbers
- bundle everything into one package, forcing you to take HTTP clients when you only need a regex

This project takes a different approach: **two focused NuGet packages with zero external dependencies**, designed to be composed rather than consumed whole.

---

## Packages

### `VatEvidence.Core`

Local EU VAT number validation. No network calls, no external dependencies.

```csharp
var result = VatNumberValidator.Validate("HR12345678901");
// VatValidationResult { IsValid, CountryCode, NormalizedVat, ErrorReason }
```

Covers all 27 EU member states with format validation and checksum verification where applicable (ISO 7064 MOD-11-10, MOD-97, MOD-11, Luhn). Results are deterministic, allocation-efficient, and safe to call from hot paths.

### `VatEvidence.Vies`

EU VIES active-status verification. Calls the [EU Commission VIES REST API](https://ec.europa.eu/taxation_customs/vies/) to confirm whether a VAT number belongs to an active, registered business.

```csharp
var vies = await viesClient.CheckAsync("HR", "12345678901", cancellationToken);
// ViesResult { IsActive, Name, Address, RequestDate, ErrorReason }
```

Intentionally decoupled from `VatEvidence.Core` — take one, the other, or both.

---

## Validation flow

```
Raw input: "HR12345678901"
         │
         ▼
VatEvidence.Core                 ← fast, local, no network
VatNumberValidator.Validate()
         │
    ┌────┴────┐
  INVALID   VALID
    │          │
  return     VatEvidence.Vies    ← only if format passes
  error      ViesClient.CheckAsync()
                  │
             IsActive, Name, Address
```

This two-step design means invalid numbers never reach the network, and VIES quota is not wasted on typos or test data.

---

## Stack

- **.NET 10**
- **ASP.NET Core Minimal API** — reference implementation
- **xUnit** — per-country unit tests

---

## Project structure

```
eu-vat-evidence/
│
├── src/
│   ├── VatEvidence.Core/      # NuGet — validator, country classification
│   └── VatEvidence.Vies/      # NuGet — VIES HTTP client
│
├── VatEvidence.Web/           # Minimal API reference implementation
└── VatEvidence.Test.Unit/     # Per-country validator tests
```

---

## Country coverage

All 27 EU member states, validated against official EU VAT format specifications:

| Prefix | Country | Checksum |
|--------|---------|----------|
| AT | Austria | — |
| BE | Belgium | MOD-97 |
| BG | Bulgaria | — |
| CY | Cyprus | — |
| CZ | Czech Republic | — |
| DE | Germany | MOD-11-10 |
| DK | Denmark | MOD-11 |
| EE | Estonia | — |
| EL | Greece | MOD-11 |
| ES | Spain | — |
| FI | Finland | MOD-11 |
| FR | France | — |
| HR | Croatia | ISO 7064 MOD-11-10 |
| HU | Hungary | — |
| IE | Ireland | — |
| IT | Italy | Luhn |
| LT | Lithuania | — |
| LU | Luxembourg | MOD-89 |
| LV | Latvia | — |
| MT | Malta | — |
| NL | Netherlands | — |
| PL | Poland | Weighted |
| PT | Portugal | — |
| RO | Romania | — |
| SE | Sweden | — |
| SI | Slovenia | MOD-11 |
| SK | Slovakia | MOD-11 |

---

## Getting started

```bash
dotnet add package VatEvidence.Core
dotnet add package VatEvidence.Vies   # optional
```

```csharp
// Step 1 — local validation, always
var result = VatNumberValidator.Validate(rawInput);

if (!result.IsValid)
    return BadRequest(result.ErrorReason);

// Step 2 — VIES check, only when needed
var vies = await _viesClient.CheckAsync(
    result.CountryCode!,
    result.NormalizedVat![2..],
    cancellationToken);
```

Register VIES client in DI:

```csharp
builder.Services.AddHttpClient<IViesClient, ViesClient>();
```

---

## Evidence records

Beyond validation, the project maintains an append-only SHA-256 hash-chain of VAT check events — designed for audit trails in invoicing and compliance systems where you need to prove a VAT number was valid at the time of a transaction.

---

## License

MIT — see [LICENSE](LICENSE).

---

## JetBrains Open Source

This project is developed with the support of a [JetBrains Open Source license](https://www.jetbrains.com/community/opensource/). JetBrains supports open-source developers with free access to their professional tools.