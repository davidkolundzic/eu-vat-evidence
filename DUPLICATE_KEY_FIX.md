# Duplicate Key Violation Fix - provider_events

## Problem

**Greška:**
```
23505 duplicate key violates unique constraint "ix_provider_events_workspace_id_provider_mode_provider_event_id"
```

**Scenario:**
Kada Stripe pošalje **duplicate webhook** (retry, resend, ili isti event više puta), događalo se sledeće:

1. ✅ `SaveOrLoadEventAsync()` pokuša insertovati `providerEvent`
2. ❌ DB baci `UniqueViolation` (23505)
3. ✅ Catch blok uhvati exception i učita postojeći event sa `AsNoTracking()`
4. ❌ **Problem:** Failed `providerEvent` ostane u EF ChangeTracker-u u `Added` stanju
5. ❌ Kasnije, `ProcessStripeTransactionAsync` pozove `SaveChangesAsync()` za Transaction/Evidence
6. ❌ EF pokuša ponovo da insertuje failed `providerEvent` → **opet 23505 exception → 500 error**

---

## Root Cause

**EF ChangeTracker ne zna da je insert failovao** jer je exception uhvaćen u našem kodu. Entitet ostaje tracked u `Added` stanju, pa svaki sledeći `SaveChangesAsync()` pokušava insert.

---

## Fix

### Dodao sam detachment failed entiteta iz ChangeTracker-a:

```csharp
catch (DbUpdateException ex)
  when (ex.InnerException is PostgresException pex
        && pex.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(pex.ConstraintName,
            "ix_provider_events_workspace_id_provider_mode_provider_event_id",
            StringComparison.Ordinal))
{
  // ✅ KLJUČNO: detach failed insert iz ChangeTracker-a
  if (_db is DbContext dbContext)
  {
    dbContext.Entry(providerEvent).State = EntityState.Detached;
  }

  // Učitaj postojeći event iz DB
  var existing = await _db.ProviderEvents
    .AsNoTracking()
    .SingleAsync(x =>
      x.WorkspaceId == cmd.WorkspaceId &&
      x.Provider == providerKind &&
      x.Mode == mode &&
      x.ProviderEventId == cmd.EventId, ct);

  _logger.LogInformation("Duplicate provider_event detected...");

  return existing;
}
```

---

## Zašto cast na `DbContext`?

`IAppDbContext` interface ne izlaže `Entry()` metodu (po dizajnu - interface je minimalan).

**Opcije:**
1. ✅ **Cast na `DbContext`** (manje invazivno, brža implementacija)
2. ❌ Dodati `Entry()` u `IAppDbContext` interface (više posla, širi impact)

**Odabrao sam opciju 1** jer je specifičan edge case i ne zahteva promenu interface-a.

---

## Verifikacija

### Scenario 1: Prvi webhook (uspešan insert)
```
POST /api/webhooks/stripe/test?workspace_id=xxx
EventId: evt_123
```

**Rezultat:**
- ✅ Insert `providerEvent` u DB
- ✅ Process transaction
- ✅ HTTP 200 OK

---

### Scenario 2: Duplicate webhook (pre fix-a)
```
POST /api/webhooks/stripe/test?workspace_id=xxx
EventId: evt_123  # isti event ID
```

**Pre fix-a:**
- ✅ Catch unique violation u `SaveOrLoadEventAsync`
- ✅ Load existing event
- ❌ **Failed `providerEvent` ostane u ChangeTracker-u**
- ❌ `ProcessStripeTransactionAsync` → `SaveChangesAsync()` → **23505 again → 500 error**

**Posle fix-a:**
- ✅ Catch unique violation
- ✅ **Detach failed `providerEvent` iz ChangeTracker-a**
- ✅ Load existing event
- ✅ `ProcessStripeTransactionAsync` → `SaveChangesAsync()` → **uspešno (nema failed entiteta)**
- ✅ HTTP 200 OK

---

## Testiranje

### Test 1: Simuliraj duplicate webhook sa Stripe CLI
```bash
# Terminal 1: Listen webhooks
stripe listen --forward-to http://localhost:5000/api/webhooks/stripe/test?workspace_id=xxx

# Terminal 2: Triggeruj isti event 2x
stripe trigger payment_intent.succeeded
stripe events resend evt_xxx  # resend istog eventa
```

**Očekivano:**
- Prvi poziv: `[INF] Created transaction {txId} for PI pi_xxx`
- Drugi poziv: `[INF] Duplicate provider_event detected: EventId=evt_xxx`
- **Oba zahteva vraćaju HTTP 200 OK** (ne 500)

---

### Test 2: Proveri DB
```sql
SELECT 
  provider_event_id,
  processing_status,
  error
FROM provider_events
WHERE provider_event_id = 'evt_xxx';
```

**Očekivano:**
```
| provider_event_id | processing_status | error |
|-------------------|-------------------|-------|
| evt_xxx           | Processed         | NULL  |
```

**Samo 1 red u DB** (duplicate nije kreirao novi red).

---

### Test 3: Proveri logove
```
[INF] Duplicate provider_event detected: EventId=evt_xxx, WorkspaceId=yyy, Mode=test. Loaded existing from DB.
[INF] Duplicate event evt_xxx already processed, skipping
```

**Nema 500 error-a**.

---

## Related Issues

### Issue 1: Stripe Retry Behavior
Stripe automatski retry-uje webhook ako dobije:
- HTTP 500 (server error)
- HTTP 429 (rate limit)
- Timeout (>30s)

**Pre fix-a:** Duplicate webhook → 500 → Stripe retries → opet 500 → infinite retry loop  
**Posle fix-a:** Duplicate webhook → 200 OK (skipped) → Stripe stops retrying ✅

---

### Issue 2: Parallel Webhook Processing
Ako 2 Stripe webhook zahteva stignu **istovremeno** (npr. preko load balancer-a):

**Request 1:**
1. Insert `providerEvent` → ✅ uspešno
2. Process transaction → ✅ uspešno

**Request 2 (parallel):**
1. Insert `providerEvent` → ❌ unique violation
2. **Detach failed entitet** → ✅
3. Load existing event → ✅ (ProcessingStatus = Received ili Processed)
4. Skip processing (already processed) → ✅

**Oba zahteva završavaju sa 200 OK.**

---

## Performance Impact

**Minimal:**
- `Entry().State = Detached` je O(1) operacija (samo menja enum vrednost)
- Cast `_db is DbContext` je runtime type check (negligible overhead)
- Samo se izvršava u **catch bloku** (edge case, retko se dešava)

**No impact on happy path** (99% webhook-a).

---

## Security Considerations

**Idempotency:**
- ✅ `provider_events` unique constraint sprečava duplikate
- ✅ `transactions` unique constraint sprečava duplikate
- ✅ `evidence_records` unique constraint sprečava duplikate

**Retry Safety:**
- ✅ Ako Stripe retry-uje webhook, rezultat je isti (idempotent)
- ✅ Nema side-effecta (duplicate processing je skipped)

---

## Monitoring

**Key metrics to track:**
1. **Duplicate event rate:**
   ```sql
   SELECT COUNT(*) FROM provider_events
   WHERE processing_status = 'Processed'
     AND error IS NULL
     AND (log contains 'Duplicate provider_event detected');
   ```

2. **Failed inserts (should be 0 now):**
   ```sql
   SELECT COUNT(*) FROM provider_events
   WHERE processing_status = 'Failed'
     AND error LIKE '%23505%';
   ```

---

## Related Files

- **Fixed file:** `VatEvidence.Application\Webhooks\StripeWebhookProcessor.cs`
- **Test guide:** `IP_COUNTRY_EXTRACTION_GUIDE.md`
- **Refactor summary:** `REFACTOR_SUMMARY.md`

---

## Questions?

**Q: Zašto ne koristim `ChangeTracker.Clear()`?**  
A: `Clear()` bi obrisao **SVE** tracked entitete, uključujući `Transaction` i `Evidence` koje hoću da sačuvam. `Detach()` cilja samo failed `providerEvent`.

**Q: Da li `AsNoTracking()` nije dovoljno?**  
A: `AsNoTracking()` sprečava tracking **učitanog** entiteta, ali ne utiče na već tracked failed insert. Moram eksplicitno detach-ovati.

**Q: Šta ako cast na `DbContext` failuje?**  
A: U praksi nikad (implementation je uvek `AppDbContext : DbContext`). Ako bi failovao, kod bi nastavio bez detachment-a (edge case u edge case-u), ali bi logging još uvek radio.
