# Quick Test - Duplicate Webhook Fix

## Test Scenario: Duplicate Webhook

### Setup
```bash
# Terminal 1: Start app
dotnet run --project VatEvidence.Web

# Terminal 2: Stripe CLI listen
stripe listen --forward-to http://localhost:5000/api/webhooks/stripe/test?workspace_id=YOUR_GUID_HERE
```

---

### Test 1: Prvi webhook (uspešan)
```bash
# Terminal 3: Triggeruj event
stripe trigger payment_intent.succeeded
```

**Očekivani logovi:**
```
[INF] Created transaction {txId} for PI pi_xxx
[INF] Appended billing country evidence HR for transaction {txId}
[INF] Canonical fetch for PI pi_xxx: billing=HR, ip=HR
```

**HTTP Response:** `200 OK`

---

### Test 2: Duplicate webhook (resend istog eventa)
```bash
# U Stripe CLI output-u, pronađi Event ID (npr. evt_123)
# Pa resend-uj:
stripe events resend evt_123
```

**Očekivani logovi:**
```
[INF] Duplicate provider_event detected: EventId=evt_123, WorkspaceId=xxx, Mode=test. Loaded existing from DB.
[INF] Duplicate event evt_123 already processed, skipping
```

**HTTP Response:** `200 OK` ✅ (ne 500!)

---

### Test 3: Proveri DB
```sql
SELECT 
  provider_event_id,
  processing_status,
  error,
  received_utc
FROM provider_events
WHERE provider_event_id = 'evt_123';
```

**Očekivano:**
- Samo **1 red** u DB (duplicate nije kreirao novi)
- `processing_status = 'Processed'`
- `error IS NULL`

---

### Test 4: Parallel webhooks (simulacija race condition)
```bash
# Terminal 3 i 4 istovremeno (rucno, ili script):
curl -X POST http://localhost:5000/api/webhooks/stripe/test?workspace_id=xxx \
  -H "Content-Type: application/json" \
  -H "Stripe-Signature: ..." \
  -d '{"id":"evt_parallel","type":"payment_intent.succeeded",...}' &

curl -X POST http://localhost:5000/api/webhooks/stripe/test?workspace_id=xxx \
  -H "Content-Type: application/json" \
  -H "Stripe-Signature: ..." \
  -d '{"id":"evt_parallel","type":"payment_intent.succeeded",...}' &
```

**Očekivano:**
- Oba zahteva vraćaju `200 OK`
- Logovi pokazuju:
  - Request 1: `[INF] Created transaction...`
  - Request 2: `[INF] Duplicate provider_event detected...`

---

## Success Criteria

✅ **Prvi webhook:** HTTP 200 OK, transaction created  
✅ **Duplicate webhook:** HTTP 200 OK, skipped processing  
✅ **Parallel webhooks:** Oba 200 OK, samo 1 transaction u DB  
✅ **DB check:** Samo 1 `provider_event` red per event ID  
✅ **No 500 errors** u logovima

---

## If Test Fails

### Scenario: Duplicate webhook vraća 500
**Razlog:** Fix nije primenjen ili `_db is DbContext` cast failuje

**Debug:**
```csharp
// Dodaj breakpoint u catch bloku:
catch (DbUpdateException ex) when (...)
{
  if (_db is DbContext dbContext) // ← Breakpoint ovde
  {
    dbContext.Entry(providerEvent).State = EntityState.Detached;
  }
  // ...
}
```

### Scenario: Parallel webhooks kreiraju 2 transakcije
**Razlog:** Race condition u `ProcessStripeTransactionAsync`

**Fix:** Proveri `IsTransactionUniqueViolation` catch blok u transaction upsert logici.

---

## Cleanup

```bash
# Stop Stripe CLI
Ctrl+C

# Stop app
Ctrl+C

# Clear test data (optional)
DELETE FROM provider_events WHERE mode = 'Test';
DELETE FROM evidence_records WHERE transaction_id IN (SELECT id FROM transactions WHERE mode = 'Test');
DELETE FROM transactions WHERE mode = 'Test';
```
