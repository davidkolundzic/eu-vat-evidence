# Evidence Sequence Migration - Quick Guide

## Primjena migracije

### 1. Provjeri pending migracije
```bash
dotnet ef migrations list --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

Trebalo bi vidjeti:
```
20260129161251_InitialWithSnakeCase (Applied)
20260202200000_AddEvidenceSequenceAndIdempotency (Pending)
```

### 2. Primijeni migraciju
```bash
dotnet ef database update --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

### 3. Verifikacija

Provjeri da li je `sequence` column kreiran i backfilled:
```sql
-- Provjeri strukturu
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'evidence_records' 
AND column_name = 'sequence';

-- Provjeri da li postoje vrijednosti
SELECT transaction_id, sequence, captured_utc, evidence_type
FROM evidence_records
ORDER BY transaction_id, sequence;

-- Provjeri indexe
SELECT indexname, indexdef 
FROM pg_indexes 
WHERE tablename = 'evidence_records';
```

O?ekivani indexi:
- `ux_evidence_records_tx_sequence` (UNIQUE)
- `ux_evidence_records_tx_type_source` (UNIQUE)
- `ix_evidence_records_transaction_id_captured_utc`

---

## Rollback (ako nešto po?e po zlu)

```bash
# Vrati na prethodnu migraciju
dotnet ef database update 20260129161251_InitialWithSnakeCase --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

**UPOZORENJE**: Ovo ?e obrisati:
- `sequence` column
- `ux_evidence_records_tx_sequence` index
- `ux_evidence_records_tx_type_source` index

Ali ne?e obrisati postoje?e evidence zapise.

---

## Test scenariji nakon migracije

### Test 1: Provjeri da sequence radi
```bash
# Pošalji webhook (koristi webhook-stripe.http fajl)
POST http://localhost:5000/api/webhooks/stripe/test?workspace_id=YOUR_WORKSPACE_ID
```

Zatim provjeri DB:
```sql
SELECT id, sequence, evidence_type, source_ref 
FROM evidence_records 
WHERE transaction_id = 'TRANSACTION_ID'
ORDER BY sequence;
```

Trebalo bi vidjeti:
```
sequence | evidence_type | source_ref
---------|---------------|------------
1        | 0 (Billing)   | evt_123...
2        | 1 (IP)        | evt_123...
```

### Test 2: Provjeri idempotency (dupli webhook)
```bash
# Pošalji isti webhook 2x sa istim evt_id
POST http://localhost:5000/api/webhooks/stripe/test?workspace_id=YOUR_WORKSPACE_ID
(isti payload)
```

Rezultat:
- Prvi: Status 200, kreira sve zapise
- Drugi: Status 200, vra?a "Duplicate event", NE kreira dupli evidence

Provjeri DB:
```sql
SELECT COUNT(*) FROM evidence_records WHERE source_ref = 'evt_123...';
-- Trebalo bi biti 2 (Billing + IP), NE 4
```

### Test 3: Provjeri retry failanog event-a
```sql
-- Ru?no ozna?i event kao Failed
UPDATE provider_events 
SET processing_status = 2 -- Failed
WHERE provider_event_id = 'evt_123...';
```

```bash
# Pošalji isti webhook ponovo
POST http://localhost:5000/api/webhooks/stripe/test?workspace_id=YOUR_WORKSPACE_ID
```

Rezultat:
- Status 200
- Event se reprocessira i ozna?ava kao Processed
- Evidence se NE duplicira (hvala idempotency constraint-u)

---

## Production Checklist

Prije deploy-a u produkciju:

- [ ] Migracija testirana na dev/staging bazi
- [ ] Backfill SQL verificiran (postoje?i redovi dobili sequence)
- [ ] UNIQUE constrainti ne kreiraju konflikte sa postoje?im podacima
- [ ] Webhook retry testiran (šalje duplicate event)
- [ ] Transient error handling testiran (simuliraj deadlock)
- [ ] Rollback plan spreman i testiran

---

## Troubleshooting

### Problem: UNIQUE constraint violation na `ux_evidence_records_tx_type_source`

**Uzrok**: Postoje dupli zapisi prije migracije (isti tx + type + source_ref)

**Rješenje**:
```sql
-- Prona?i duplikate
SELECT transaction_id, evidence_type, source_ref, COUNT(*) 
FROM evidence_records 
GROUP BY transaction_id, evidence_type, source_ref 
HAVING COUNT(*) > 1;

-- Zadrži samo prvi (najstariji)
DELETE FROM evidence_records 
WHERE id NOT IN (
  SELECT MIN(id) 
  FROM evidence_records 
  GROUP BY transaction_id, evidence_type, source_ref
);
```

### Problem: Sequence nula za neke redove

**Uzrok**: Backfill SQL nije izvršen ili je failao

**Rješenje**:
```sql
-- Ru?no backfill
WITH ordered AS (
  SELECT 
    id, 
    transaction_id,
    ROW_NUMBER() OVER (PARTITION BY transaction_id ORDER BY captured_utc, id) AS seq
  FROM evidence_records
  WHERE sequence = 0
)
UPDATE evidence_records er
SET sequence = o.seq
FROM ordered o
WHERE er.id = o.id;
```

### Problem: "FOR UPDATE" deadlock

**Uzrok**: Paralelni webhook-ovi na razli?ite transakcije locka-ju redove u razli?itom redoslijedu

**Rješenje**:
- Deadlock je transient greška
- Controller vra?a 500
- Stripe automatski retry-a
- Drugi pokušaj prolazi (idempotentno)

**Preventivno** (opciono): Dodaj row-level lock timeout:
```csharp
await _db.FromSqlInterpolated<Transaction>($@"
  SET lock_timeout = '5s';
  SELECT * FROM transactions WHERE id = {command.TransactionId} FOR UPDATE
")
```

---

**Kreirao**: GitHub Copilot  
**Datum**: 2025-02-02  
**Migracija**: `20260202200000_AddEvidenceSequenceAndIdempotency`
