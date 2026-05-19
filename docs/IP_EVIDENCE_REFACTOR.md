# IP Evidence Refactor Summary

## Problem

**BEFORE:** Webhooks attempted to create IP evidence from `CF-IPCountry` header, but webhooks originate from **Stripe servers**, not buyer browsers. This resulted in incorrect IP evidence (Stripe server IPs, not buyer IPs).

## Solution

**AFTER:** IP evidence is captured ONLY from **buyer-facing checkout requests** via new `StripeCheckoutController`. Webhooks now create **only billing evidence** from Stripe PaymentIntent canonical fetch.

---

## Changes Made

### 1. **StripeWebhookController.cs**

**REMOVED:**
```csharp
var ipCountryHint = GetIpCountryHint();
```

**CHANGED:**
```csharp
IpCountryHint: null // Webhooks don't have buyer IP
```

**REASON:** Webhooks originate from Stripe infrastructure (US/EU servers), not buyer browsers.

---

### 2. **StripeWebhookProcessor.cs**

**REMOVED entire block:**
```csharp
// 7) Append IP evidence (if available)
if (!string.IsNullOrWhiteSpace(ipCountry))
{
  var ipSnapshot = StripePayloadExtractor.CreateIpSnapshot(...);
  await _evidenceAppendService.AppendAsync(...);
  ...
}
```

**KEPT:**
- Billing evidence append (step 5)
- Status evaluation (step 6)
- Idempotency logic
- Transaction upsert

**REASON:** IP evidence must come from buyer request, not webhook.

---

### 3. **StripeCheckoutController.cs (NEW)**

**CREATED:** `/api/stripe/checkout/session`

**Behavior:**
1. Extract buyer IP from `CF-IPCountry` header (Cloudflare GeoIP)
2. Create Stripe Checkout Session
3. Upsert Transaction
4. **Append IP evidence** (EvidenceType.Ipcountry, source_ref: "cf-ipcountry")
5. Wrap in DB transaction (required by `EvidenceAppendService`)

**CRITICAL:** This is the **only place** where IP evidence is created.

---

### 4. **checkout-test.html (NEW)**

Minimal buyer-facing test page:
- Form to create checkout session
- Calls `/api/stripe/checkout/session`
- Redirects to Stripe Checkout

---

## Flow Comparison

### BEFORE (WRONG)

```
Browser → Stripe Checkout → Payment
         ↓
         Webhook (from Stripe server) → StripeWebhookProcessor
         ↓
         Creates IP evidence (WRONG: Stripe server IP, not buyer IP)
         Creates billing evidence
```

### AFTER (CORRECT)

```
Browser → POST /api/stripe/checkout/session (StripeCheckoutController)
         ↓
         Creates IP evidence (CORRECT: buyer's actual IP from CF-IPCountry)
         ↓
         Redirects to Stripe Checkout → Payment
         ↓
         Webhook (from Stripe server) → StripeWebhookProcessor
         ↓
         Creates billing evidence ONLY
```

---

## Evidence Source Mapping

| Evidence Type      | Source                          | Created By                 | SourceRef                        |
|--------------------|---------------------------------|----------------------------|----------------------------------|
| **Ipcountry**      | CF-IPCountry header (buyer req) | StripeCheckoutController   | `cf-ipcountry`                   |
| **Billingcountry** | Stripe Charge billing_details   | StripeWebhookProcessor     | `stripe:charge:{chId}:billing`   |

---

## Status Evaluation (UNCHANGED)

**Logic remains identical:**
- **Ok:** Billing country == IP country
- **Mismatch:** Billing country != IP country
- **Insufficient:** Missing billing or IP evidence

**Example SQL verification:**
```sql
SELECT 
  t.provider_transaction_id,
  e.evidence_type,
  e.country_code,
  e.source_ref,
  e.sequence
FROM evidence_records e
JOIN transactions t ON t.id = e.transaction_id
WHERE t.provider_transaction_id = 'pi_xxx'
ORDER BY e.sequence;
```

**Expected output:**
```
pi_xxx | Ipcountry       | HR | cf-ipcountry                  | 1
pi_xxx | Billingcountry  | HR | stripe:charge:ch_xxx:billing  | 2
```

---

## Testing

### 1. Manual Test
1. Open `/checkout-test.html`
2. Fill workspace ID, amount, email
3. Click "Create Checkout Session"
4. Complete payment in Stripe Checkout
5. Verify evidence with SQL query (see `EVIDENCE_VERIFICATION.sql`)

### 2. Expected Results
- Transaction has 2 evidence records:
  - **Ipcountry** (sequence 1, source: cf-ipcountry) ← from checkout controller
  - **Billingcountry** (sequence 2, source: stripe:charge:...) ← from webhook
- Status: `Ok` (if countries match) or `Mismatch` (if different)

---

## Constraints Preserved

✅ **No schema changes**  
✅ **No migration changes**  
✅ **Idempotency preserved** (webhook can be replayed safely)  
✅ **Evidence chain hash preserved** (EvidenceAppendService transactional logic intact)  
✅ **Existing business rules unchanged** (status evaluation, country classification)  

---

## Files Changed

| File                                      | Change Type | Lines Changed |
|-------------------------------------------|-------------|---------------|
| `StripeWebhookController.cs`              | Modified    | -3 lines      |
| `StripeWebhookProcessor.cs`               | Modified    | -30 lines     |
| `StripeCheckoutController.cs`             | **NEW**     | +250 lines    |
| `checkout-test.html`                      | **NEW**     | +150 lines    |
| `EVIDENCE_VERIFICATION.sql`               | **NEW**     | +80 lines     |

---

## Next Steps (Future)

1. **Add retry logic** to checkout controller (Polly?)
2. **Add telemetry** (OpenTelemetry spans for evidence creation)
3. **Add caching** for Stripe API calls (IStripeCanonicalReader)
4. **Add rate limiting** to checkout endpoint
5. **Add webhook replay protection** (already idempotent, but could add explicit replay detection)

---

## Production Readiness Checklist

- [x] IP evidence ONLY from buyer requests
- [x] Webhooks create billing evidence only
- [x] Idempotency preserved
- [x] DB transactions for evidence append
- [x] Logging added (LoggerMessage)
- [x] Test page provided
- [x] SQL verification query provided
- [ ] Integration tests (TODO)
- [ ] Load testing (TODO)
- [ ] Cloudflare header validation in prod (TODO)

---

**Refactor completed:** IP evidence now correctly captured from buyer browser, webhooks only create billing evidence. System remains idempotent and transactionally safe.
