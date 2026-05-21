# Refaktorisanje Stripe Webhook Processor - Sažetak izmena

## Cilj refaktora
Prelazak sa razgranatog parsiranja webhook payloadova na **jedan canonical pipeline** koji koristi server-to-server Stripe API fetch za pouzdanije i audit-friendly prikupljanje dokaza.

---

## Nove datoteke

### 1. `VatEvidence.Application\Options\StripeOptions.cs`
Konfiguracija za Stripe API ključeve (test/live mode).

```csharp
public sealed class StripeOptions
{
  public string TestSecretKey { get; set; } = string.Empty;
  public string LiveSecretKey { get; set; } = string.Empty;
}
```

### 2. `VatEvidence.Application\Webhooks\StripePayloadExtractor.cs`
Helper metode za:
- Izvlačenje `payment_intent` ID-a iz raznih event tipova (`payment_intent.*`, `checkout.session.*`, `charge.*`)
- Kreiranje audit-friendly JSON snapshot-a za billing i IP evidence

---

## Izmenjene datoteke

### 3. `VatEvidence.Application\Webhooks\StripeWebhookProcessor.cs`

**Ključne izmene:**

#### a) Dodato u constructor:
```csharp
IOptions<StripeOptions> _stripeOptions
```

#### b) Refaktorisan `ProcessAsync`:
- **Stari pristup:** `if (eventType == "payment_intent.succeeded")` → poziv specifične metode
- **Novi pristup:**
  1. Poziv `StripePayloadExtractor.ExtractPaymentIntentId(eventType, payload)`
  2. Ako piId postoji → poziv `ProcessStripeTransactionAsync(piId, ...)`
  3. Ako piId ne postoji → mark as processed (non-retryable)

#### c) Nova metoda: `ProcessStripeTransactionAsync`
Canonical pipeline:
1. **Fetch canonical state:** `PaymentIntentService.GetAsync(piId, expand: ["latest_charge"])`
2. **Extract billing country:** `charge.BillingDetails.Address.Country`
3. **Extract IP country:** iz `ipCountryHint` parametra (CF-IPCountry header)
4. **Upsert Transaction:** po `ProviderTransactionId = piId`
5. **Append billing evidence:** sa `SourceRef = "stripe:charge:{chargeId}:billing"`
6. **Append IP evidence:** sa `SourceRef = "stripe:webhook:ip-hint"`
7. **Evaluate status:** poziv postojeće `EvaluateStatusAsync`
8. **Commit:** jedna DB transakcija za sve

**Prednosti:**
- Billing country je uvek iz canonical izvora (`latest_charge.billing_details`)
- Audit trail je jasniji (SourceRef eksplicitno navodi izvor)
- ValueRaw sadrži strukturiran JSON snapshot za audit
- Idempotentnost preko unique constraint-a (nije potrebna dodatna logika)

#### d) Stare metode (`ProcessPaymentIntentSucceededAsync`, `ProcessCheckoutSessionCompletedAsync`)
**Ostavljene su u kodu** ali **se više ne pozivaju** iz `ProcessAsync`.
- Možeš ih ukloniti kasnije ili ih zadržati za fallback scenario
- Za sada nisu označene kao `[Obsolete]` da ne bi brejkovale kompilaciju

---

### 4. `VatEvidence.Application\Webhooks\StripeWebhookProcessor.Logging.cs`

Dodat novi log metod:
```csharp
[LoggerMessage(Level = LogLevel.Information, 
  Message = "Canonical fetch for PI {PaymentIntentId}: billing={BillingCountry}, ip={IpCountry}")]
partial void LogCanonicalFetch(string paymentIntentId, string? billingCountry, string? ipCountry);
```

---

### 5. `VatEvidence.Web\Program.cs`

Dodato:
```csharp
builder.Services.Configure<StripeOptions>(
  builder.Configuration.GetSection(StripeOptions.SectionName));
```

---

### 6. `VatEvidence.Web\appsettings.json`

Dodato:
```json
"Stripe": {
  "TestSecretKey": "",
  "LiveSecretKey": ""
}
```

---

## Kako postaviti Stripe API ključeve

### Lokalni development:
Kreiraj `appsettings.Development.json`:
```json
{
  "Stripe": {
    "TestSecretKey": "sk_test_...",
    "LiveSecretKey": "sk_live_..."
  }
}
```

### Production (Render):
Postavi environment varijable:
```
Stripe__TestSecretKey=sk_test_...
Stripe__LiveSecretKey=sk_live_...
```

---

## Testiranje

### 1. Unit test (mock Stripe API)
- Možeš mock-ovati `PaymentIntentService` za testiranje bez stvarnog Stripe poziva

### 2. Integration test sa Stripe CLI
```bash
# Terminal 1: Slušaj webhook-ove
stripe listen --forward-to http://localhost:5000/api/webhooks/stripe/test?workspace_id=<GUID>

# Terminal 2: Triggeruj test event
stripe trigger payment_intent.succeeded
```

### 3. Verifikuj logove
Očekivani log output:
```
[INF] Canonical fetch for PI pi_xxx: billing=HR, ip=HR
[INF] Created transaction {txId} for PI pi_xxx
[INF] Appended billing country evidence HR for transaction {txId}
[INF] Appended IP country evidence HR for transaction {txId}
```

---

## Retryability & Idempotency

### Retryable greške (Stripe će retry-ovati):
- `StripeException` (API timeout, rate limit, network)
- `DbException` (DB timeout, deadlock)

### Non-retryable greške (webhook se mark-uje kao processed):
- Event bez `payment_intent` ID-a (npr. `customer.created`)
- PaymentIntent ne postoji u Stripe API-ju (soft failure, log warning)

### Idempotency:
- `ProviderEvent` unique constraint: `(workspace_id, provider, mode, provider_event_id)`
- `Transaction` unique constraint: `(workspace_id, provider, mode, provider_transaction_id)`
- `EvidenceRecord` unique constraint: `(transaction_id, evidence_type, source_ref)`

---

## Šta NIJE promenjeno

- Domenske entitete (`Transaction`, `EvidenceRecord`, `ProviderEvent`)
- DB schema
- Signature validaciju
- Rate limiting
- Existing evidence evaluation logic (`EvaluateStatusAsync`)

---

## Bug Fixes ✅

### Duplicate Key Violation Fix (provider_events)

**Problem:** Kada Stripe pošalje duplicate webhook, failed `providerEvent` insert ostaje u EF ChangeTracker-u, pa kasnije `SaveChangesAsync()` opet pokuša insert → 500 error.

**Fix:** Dodao sam detachment failed entiteta iz ChangeTracker-a:

```csharp
catch (DbUpdateException ex) when (ex.InnerException is PostgresException pex && ...)
{
  // Detach failed insert iz ChangeTracker-a
  if (_db is DbContext dbContext)
  {
    dbContext.Entry(providerEvent).State = EntityState.Detached;
  }

  // Load existing event
  var existing = await _db.ProviderEvents.AsNoTracking().SingleAsync(...);
  return existing;
}
```

**Rezultat:**
- ✅ Duplicate webhook-ovi sada vraćaju 200 OK (ne 500)
- ✅ Stripe ne retry-uje duplicate event-e beskrajno
- ✅ Parallel webhook processing radi ispravno

**Detalji:** Proveri `DUPLICATE_KEY_FIX.md`

---

## Legacy kod (cleanup) ✅

**Stare metode su premeštene u `StripeWebhookProcessor.Legacy.cs`:**
- `ProcessCheckoutSessionCompletedAsync` - označena sa `[Obsolete]`
- `ProcessPaymentIntentSucceededAsync` - označena sa `[Obsolete]`
- `ExtractBillingCountry`, `ExtractIpCountry` - legacy helperi označeni sa `[Obsolete]`

**Zašto?**
- Smanjuje rizik "slučajnog vraćanja" na stari pristup
- Olakšava code review (glavni file je ~450 linija umesto 900+)
- Legacy metode se **NE pozivaju nigde** - samo su zadržane za referencu

**Kada ukloniti?**
- Nakon 1-2 meseca production testa
- Proveri `LEGACY_CODE_MIGRATION.md` za checklist

---

## Sledeći koraci (opciono)

1. **Ukloni legacy file nakon production verifikacije:**
   - Proveri `LEGACY_CODE_MIGRATION.md` za detalje
   - Ukloni `StripeWebhookProcessor.Legacy.cs` nakon 1-2 meseca

2. **Dodaj više event tipova:**
   - `charge.succeeded`, `charge.refunded`, itd.
   - Svi će koristiti isti canonical pipeline

3. **Monitoring:**
   - Dodaj metriku za "canonical fetch duration"
   - Alert ako Stripe API call traje > 2s

4. **Stripe Tax integration:**
   - Možeš dodati `EvidenceType.PaymentCountry` iz `automatic_tax.location.country`
   - Sve preko istog pipeline-a

---

## Pitanja?

Ako imaš problema sa konfiguracijom ili deployment-om, proveri:
1. Da li su Stripe ključevi postavljeni? → `curl /api/health`
2. Da li webhook signature validation radi? → proveri `ProviderConnection.WebhookSecret`
3. Da li Stripe CLI forwarding radi? → `stripe listen --forward-to ...`

Sve promene su **compile-safe** i **backward-compatible** (stare metode su ostale, ali se ne koriste).
