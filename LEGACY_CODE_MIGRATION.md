# Legacy Code Migration - StripeWebhookProcessor

## Šta je urađeno?

### Cleanup: Premeštanje legacy koda u poseban file

Legacy metode koje su zamenjene sa novim canonical pipeline-om su premeštene iz glavnog file-a (`StripeWebhookProcessor.cs`) u `StripeWebhookProcessor.Legacy.cs` i označene sa `[Obsolete]` atributom.

---

## Premeštene metode (sada obsolete)

### 1. **ProcessCheckoutSessionCompletedAsync** 
- **Status:** `[Obsolete]`
- **Razlog:** Parsira webhook payload direktno umesto korišćenja canonical Stripe API fetch-a
- **Zamenjeno sa:** `ProcessStripeTransactionAsync` (novi canonical pipeline)

### 2. **ProcessPaymentIntentSucceededAsync**
- **Status:** `[Obsolete]`
- **Razlog:** Parsira webhook payload direktno umesto korišćenja canonical Stripe API fetch-a
- **Zamenjeno sa:** `ProcessStripeTransactionAsync` (novi canonical pipeline)

### 3. **ExtractBillingCountry** (helper)
- **Status:** `[Obsolete]`
- **Razlog:** Koristi se samo u legacy metodama
- **Zamenjeno sa:** Direktan pristup `paymentIntent.LatestCharge.BillingDetails.Address.Country`

### 4. **ExtractIpCountry** (helper)
- **Status:** `[Obsolete]`
- **Razlog:** Koristi se samo u legacy metodama  
- **Zamenjeno sa:** Direktan pristup `ipCountryHint` parametru (iz CF-IPCountry headera)

---

## Struktura file-ova

### **StripeWebhookProcessor.cs** (glavni file, ~450 linija)
✅ **Aktivan kod:**
- `ProcessAsync` - glavni entry point (koristi canonical pipeline)
- `SaveOrLoadEventAsync` - idempotent event storage
- **`ProcessStripeTransactionAsync`** - **NOVI canonical pipeline** (Stripe API fetch)
- `EvaluateStatusAsync` - status evaluation
- Helper metode: `IsRetryable`, `IsDuplicateKeyViolation`, `IsTransactionUniqueViolation`, `IsValidCountryCode`, `ComputeSha256`

### **StripeWebhookProcessor.Legacy.cs** (legacy file, ~500 linija)
⚠️ **Obsolete kod (neće se pozivati):**
- `ProcessCheckoutSessionCompletedAsync` - legacy webhook parsing
- `ProcessPaymentIntentSucceededAsync` - legacy webhook parsing
- `ExtractBillingCountry` - legacy helper
- `ExtractIpCountry` - legacy helper

---

## Zašto je ovo bolje?

### ❌ **Pre (razgranat pristup)**
```csharp
if (cmd.EventType == "payment_intent.succeeded")
{
  await ProcessPaymentIntentSucceededAsync(...); // Parse webhook payload
}
else if (cmd.EventType == "checkout.session.completed")
{
  await ProcessCheckoutSessionCompletedAsync(...); // Parse webhook payload
}
```

**Problemi:**
- ❌ Razgranat kod (2+ različite grane)
- ❌ Različiti izvori podataka (webhook payloadovi)
- ❌ Audit rizik (nema canonical source of truth)
- ❌ Teško održavanje (duplikacija logike)

### ✅ **Posle (canonical pipeline)**
```csharp
var piId = StripePayloadExtractor.ExtractPaymentIntentId(cmd.EventType, cmd.PayloadJson);
if (!string.IsNullOrWhiteSpace(piId))
{
  await ProcessStripeTransactionAsync(piId, ...); // Fetch canonical state from Stripe API
}
```

**Prednosti:**
- ✅ **Jedan pipeline** za sve event tipove
- ✅ **Canonical source** (Stripe API, ne webhook payload)
- ✅ **Audit-friendly** (eksplicitni SourceRef, structured snapshots)
- ✅ **Lakše održavanje** (jedna metoda za sve)

---

## Kada ukloniti legacy kod?

**Preporuka:** Ukloni nakon **1-2 meseca production testa** kada si siguran da novi canonical pipeline radi stabilno.

### Checklist pre brisanja:
- [ ] Canonical pipeline radi stabilno u production
- [ ] Nema potrebe za rollback na legacy metode
- [ ] Proveri da nisu reference-irane nigde u code-bazi (npr. u testovima)
- [ ] Ukloni `StripeWebhookProcessor.Legacy.cs` file
- [ ] Proveri build i CI/CD pipeline

---

## Kako testirati da legacy metode nisu korištene?

1. **Build sa warnings as errors:**
```bash
dotnet build /p:TreatWarningsAsErrors=true
```

Ako legacy metode nisu korištene, build će proći jer su označene sa `[Obsolete]` ali se **ne pozivaju nigde**.

2. **Search codebase:**
```bash
git grep "ProcessCheckoutSessionCompletedAsync"
git grep "ProcessPaymentIntentSucceededAsync"
```

Ako nema match-eva van Legacy file-a → safe za brisanje.

3. **Production monitoring:**
- Prati logove za `LogCanonicalFetch` → potvrđuje da se novi pipeline koristi
- Prati metrics za `provider_events` status → sve treba biti `Processed`

---

## Pitanja?

Ako imaš problema ili sumnju:
1. Proveri `REFACTOR_SUMMARY.md` za detaljan opis canonical pipeline-a
2. Proveri `STRIPE_CONFIG.md` za konfiguraciju Stripe API ključeva
3. Kontaktiraj maintainer-a ako treba rollback

**Status:** ✅ Legacy kod je bezbedan za brisanje nakon production verifikacije (1-2 meseca).
