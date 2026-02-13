# Test Scenariji - Evidence Sequence & Idempotency

## 🔧 Setup (jednom prije testiranja)

### 1. Primijeni migraciju

```bash
cd VatEvidence.Web
dotnet ef database update --project ../VatEvidence.Infrastructure
```

### 2. Seed test workspace i connection

```sql
-- Insert test workspace
INSERT INTO workspaces (id, name, created_at)
VALUES ('11111111-1111-1111-1111-111111111111', 'Test Workspace', NOW())
ON CONFLICT (id) DO NOTHING;

-- Insert test Stripe connection
INSERT INTO provider_connections (id, workspace_id, provider, mode, webhook_secret, created_at)
VALUES (
  '22222222-2222-2222-2222-222222222222',
  '11111111-1111-1111-1111-111111111111',
  0, -- ProviderKind.Stripe
  0, -- ProviderMode.Test
  'whsec_test_secret_key_1234567890',
  NOW()
)
ON CONFLICT (id) DO NOTHING;
```

### 3. Pokreni aplikaciju

```bash
dotnet run --project VatEvidence.Web
# Ili u Visual Studio: F5
```

Aplikacija će biti dostupna na: `https://localhost:5001` ili `http://localhost:5000`

---

## ✅ Test Case 1: Basic Webhook Flow (Happy Path)

### Cilj
Verifikuj da webhook kreira transaction + 2 evidence zapisa sa ispravnim sequence vrijednostima.

### 1.1. Pošalji webhook

Koristi `webhook-stripe.http` ili curl:

```bash
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
-H "Content-Type: application/json" \
-H "Stripe-Signature: t=1234567890,v1=dummy_signature_for_testing" \
-d '{
  "id": "evt_test_001",
  "type": "payment_intent.succeeded",
  "created": 1640000000,
  "data": {
    "object": {
      "id": "pi_test_001",
      "amount": 2999,
      "currency": "eur",
      "created": 1640000000,
      "latest_charge": "ch_test_001",
      "receipt_email": "customer@example.com"
    }
  }
}'
```

**NAPOMENA**: Ako signature validation failuje, privremeno disabluj validaciju ili dodaj bypass za test secret.

### 1.2. Očekivani HTTP Response

```json
200 OK
{
  "processed": true,
  "eventId": "evt_test_001"
}
```

### 1.3. Verifikuj DB state

```sql
-- 1) Provjeri da je event kreiran
SELECT 
  id,
  provider_event_id,
  type,
  processing_status,
  error
FROM provider_events
WHERE provider_event_id = 'evt_test_001';
```

**Očekivano**:
- `processing_status` = 1 (Processed)
- `error` = NULL

```sql
-- 2) Provjeri da je transaction kreiran
SELECT 
  id,
  provider_transaction_id,
  amount_minor,
  currency,
  status,
  status_reason
FROM transactions
WHERE provider_transaction_id = 'pi_test_001';
```

**Očekivano**:
- `amount_minor` = 2999
- `currency` = 'EUR'
- `status` = 0 (Ok)
- `status_reason` = 'Evidence OK (billing matches IP)'

```sql
-- 3) Provjeri evidence zapise sa SEQUENCE
SELECT 
  id,
  transaction_id,
  sequence,
  evidence_type,
  country_code,
  source_ref,
  record_hash,
  prev_record_hash
FROM evidence_records
WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_001')
ORDER BY sequence;
```

**Očekivano**:
```
sequence | evidence_type | country_code | source_ref     | prev_record_hash
---------|---------------|--------------|----------------|------------------
1        | 0 (Billing)   | US           | evt_test_001   | NULL (head)
2        | 1 (IP)        | US           | evt_test_001   | <hash_from_seq_1>
```

✅ **PASS kriterij**: 
- Event Processed ✅
- Transaction status = Ok ✅
- 2 evidence zapisa sa sequence 1,2 ✅
- record[2].prev_record_hash == record[1].record_hash ✅

---

## 🔄 Test Case 2: Duplicate Webhook (Idempotency)

### Cilj
Pošalji **isti webhook 2x** i verifikuj da se **ne kreiraju dupli zapisi**.

### 2.1. Pošalji isti webhook ponovo

```bash
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
-H "Content-Type: application/json" \
-H "Stripe-Signature: t=1234567890,v1=dummy_signature_for_testing" \
-d '{
  "id": "evt_test_001",
  "type": "payment_intent.succeeded",
  "created": 1640000000,
  "data": {
    "object": {
      "id": "pi_test_001",
      "amount": 2999,
      "currency": "eur",
      "created": 1640000000
    }
  }
}'
```

### 2.2. Očekivani Response

```json
200 OK
{
  "processed": true,
  "eventId": "evt_test_001"
}
```

Ali u logovima trebalo bi biti:
```
[Information] Duplicate event evt_test_001 already processed, skipping
```

### 2.3. Verifikuj da NEMA duplikata

```sql
-- Provjeri da postoji SAMO 1 event
SELECT COUNT(*) as event_count
FROM provider_events
WHERE provider_event_id = 'evt_test_001';
```

**Očekivano**: `event_count` = 1

```sql
-- Provjeri da postoje SAMO 2 evidence zapisa (ne 4!)
SELECT COUNT(*) as evidence_count
FROM evidence_records
WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_001');
```

**Očekivano**: `evidence_count` = 2

✅ **PASS kriterij**:
- Event count = 1 (ne 2) ✅
- Evidence count = 2 (ne 4) ✅
- HTTP 200 vraćen ✅

---

## 🔁 Test Case 3: Failed Event Retry (Reprocessing)

### Cilj
Simuliraj scenario gdje event ranije failao, pa se retry-a i uspješno procesuira.

### 3.1. Ručno označi event kao Failed

```sql
-- Označi event kao Failed
UPDATE provider_events
SET 
  processing_status = 2,  -- Failed
  error = 'Simulated error for testing'
WHERE provider_event_id = 'evt_test_001';
```

### 3.2. Pošalji isti webhook ponovo

```bash
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
-H "Content-Type: application/json" \
-H "Stripe-Signature: t=1234567890,v1=dummy_signature_for_testing" \
-d '{
  "id": "evt_test_001",
  "type": "payment_intent.succeeded",
  "created": 1640000000,
  "data": {
    "object": {
      "id": "pi_test_001",
      "amount": 2999,
      "currency": "eur",
      "created": 1640000000
    }
  }
}'
```

### 3.3. Očekivani Behavior

Event trebalo bi **reprocessirati** (nije skip-an), ali evidence **ne treba duplirat**.

```sql
-- Provjeri da je event sada Processed
SELECT processing_status, error
FROM provider_events
WHERE provider_event_id = 'evt_test_001';
```

**Očekivano**:
- `processing_status` = 1 (Processed)
- `error` = NULL (očišćeno)

```sql
-- Evidence i dalje samo 2 zapisa
SELECT COUNT(*) as evidence_count
FROM evidence_records
WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_001');
```

**Očekivano**: `evidence_count` = 2 (idempotency zaštita radila!)

✅ **PASS kriterij**:
- Event status promijenjen Failed → Processed ✅
- Evidence NOT duplicated ✅
- Transaction status refresh (ako je bio Insufficient) ✅

---

## 🏁 Test Case 4: Parallel Webhooks (Race Condition)

### Cilj
Simultano pošalji 2 različita webhooks za 2 različite transakcije i verifikuj sequence.

### 4.1. Setup - Pošalji 2 webhooks simultano

**Terminal 1:**
```bash
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
-H "Content-Type: application/json" \
-H "Stripe-Signature: t=1234567890,v1=dummy" \
-d '{
  "id": "evt_test_parallel_A",
  "type": "payment_intent.succeeded",
  "created": 1640000001,
  "data": {
    "object": {
      "id": "pi_test_parallel_A",
      "amount": 5000,
      "currency": "eur",
      "created": 1640000001
    }
  }
}' &
```

**Terminal 2 (istovremeno):**
```bash
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
-H "Content-Type: application/json" \
-H "Stripe-Signature: t=1234567890,v1=dummy" \
-d '{
  "id": "evt_test_parallel_B",
  "type": "payment_intent.succeeded",
  "created": 1640000002,
  "data": {
    "object": {
      "id": "pi_test_parallel_B",
      "amount": 7500,
      "currency": "gbp",
      "created": 1640000002
    }
  }
}' &
```

### 4.2. Verifikuj sequence integrity

```sql
-- Transaction A evidence
SELECT sequence, evidence_type, record_hash, prev_record_hash
FROM evidence_records
WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_parallel_A')
ORDER BY sequence;
```

**Očekivano**:
```
sequence | prev_record_hash
---------|------------------
1        | NULL
2        | <hash_of_seq_1>
```

```sql
-- Transaction B evidence
SELECT sequence, evidence_type, record_hash, prev_record_hash
FROM evidence_records
WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_parallel_B')
ORDER BY sequence;
```

**Očekivano**:
```
sequence | prev_record_hash
---------|------------------
1        | NULL
2        | <hash_of_seq_1>
```

✅ **PASS kriterij**:
- Oba transakciona imaju sequence 1,2 (ne 1,3 ili 1,4 iz-za race-a) ✅
- Hash chain validan za oba ✅
- FOR UPDATE lock spriječio race condition ✅

---

## 🔗 Test Case 5: Hash Chain Integrity

### Cilj
Verifikuj da EvidenceChainVerifier prolazi nakon svih ovih test-ova.

### 5.1. Ručna SQL verifikacija

```sql
WITH chain AS (
  SELECT 
    sequence,
    record_hash,
    prev_record_hash,
    LAG(record_hash) OVER (ORDER BY sequence) as expected_prev
  FROM evidence_records
  WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_001')
)
SELECT 
  sequence,
  CASE 
    WHEN sequence = 1 THEN prev_record_hash IS NULL
    ELSE prev_record_hash = expected_prev
  END as is_chain_valid
FROM chain;
```

**Očekivano**: Svi redovi `is_chain_valid` = `true`

### 5.2. Provjeri sve transakcije odjednom

```sql
WITH chain AS (
  SELECT 
    transaction_id,
    sequence,
    record_hash,
    prev_record_hash,
    LAG(record_hash) OVER (PARTITION BY transaction_id ORDER BY sequence) as expected_prev
  FROM evidence_records
)
SELECT 
  transaction_id,
  COUNT(*) as total_records,
  SUM(CASE 
    WHEN sequence = 1 AND prev_record_hash IS NULL THEN 1
    WHEN sequence > 1 AND prev_record_hash = expected_prev THEN 1
    ELSE 0
  END) as valid_records
FROM chain
GROUP BY transaction_id
HAVING COUNT(*) != SUM(CASE 
  WHEN sequence = 1 AND prev_record_hash IS NULL THEN 1
  WHEN sequence > 1 AND prev_record_hash = expected_prev THEN 1
  ELSE 0
END);
```

**Očekivano**: Prazan rezultat (nema broken chain-ova)

✅ **PASS kriterij**:
- Svi chain-ovi valid ✅
- prev_record_hash pointeri ispravni ✅

---

## 🚨 Test Case 6: Transient Error Handling

### Cilj
Verifikuj da controller vraća 500 za transient greške (deadlock), 200 za permanentne.

### 6.1. Test permanentne greške (bez retryable)

Privremeno modificiraj `ProcessPaymentIntentSucceededAsync` da baci exception:

```csharp
// U StripeWebhookProcessor.cs, dodaj na početak metode:
throw new ArgumentException("Invalid payment intent ID");
```

Pošalji webhook:
```bash
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
-H "Content-Type: application/json" \
-H "Stripe-Signature: t=1234567890,v1=dummy" \
-d '{"id": "evt_test_error", "type": "payment_intent.succeeded", ...}'
```

**Očekivani Response**:
```json
200 OK
{
  "processed": false,
  "error": "Invalid payment intent ID",
  "retryable": false
}
```

### 6.2. Test transient greške (retryable)

Modificiraj da baci DB exception:

```csharp
throw new DbUpdateException("40P01: deadlock detected");
```

**Očekivani Response**:
```json
500 Internal Server Error
{
  "processed": false,
  "error": "40P01: deadlock detected",
  "retryable": true
}
```

✅ **PASS kriterij**:
- Permanentna greška → HTTP 200 ✅
- Transient greška → HTTP 500 ✅
- `retryable` flag ispravan ✅

---

## 📊 Summary Verification

Nakon svih testova, izvuci summary:

```sql
-- Ukupan pregled
SELECT 
  (SELECT COUNT(*) FROM provider_events) as total_events,
  (SELECT COUNT(*) FROM provider_events WHERE processing_status = 1) as processed_events,
  (SELECT COUNT(*) FROM provider_events WHERE processing_status = 2) as failed_events,
  (SELECT COUNT(*) FROM transactions) as total_transactions,
  (SELECT COUNT(*) FROM evidence_records) as total_evidence;
```

**Očekivano** (nakon Test Case 1-5):
```
total_events | processed_events | failed_events | total_transactions | total_evidence
-------------|------------------|---------------|--------------------|----------------
3-4          | 3-4              | 0             | 3-4                | 6-8
```

```sql
-- Provjeri da nema broken sequence-ova
SELECT 
  transaction_id,
  COUNT(*) as record_count,
  MAX(sequence) as max_sequence
FROM evidence_records
GROUP BY transaction_id
HAVING COUNT(*) != MAX(sequence);
```

**Očekivano**: Prazan rezultat (sve transakcije imaju continuous sequence 1..N)

---

## 🧹 Cleanup (nakon testiranja)

```sql
-- Obriši test podatke
DELETE FROM evidence_records 
WHERE transaction_id IN (
  SELECT id FROM transactions WHERE workspace_id = '11111111-1111-1111-1111-111111111111'
);

DELETE FROM transactions WHERE workspace_id = '11111111-1111-1111-1111-111111111111';
DELETE FROM provider_events WHERE workspace_id = '11111111-1111-1111-1111-111111111111';
DELETE FROM provider_connections WHERE workspace_id = '11111111-1111-1111-1111-111111111111';
DELETE FROM workspaces WHERE id = '11111111-1111-1111-1111-111111111111';
```

---

## ✅ Final Checklist

| Test Case | Status | Notes |
|-----------|--------|-------|
| ✅ Basic webhook flow | ⬜ | Sequence 1,2 kreiran |
| ✅ Duplicate webhook idempotency | ⬜ | Nema duplikata |
| ✅ Failed event reprocessing | ⬜ | Event status update |
| ✅ Parallel webhooks (race) | ⬜ | FOR UPDATE lock radi |
| ✅ Hash chain integrity | ⬜ | Verifier prolazi |
| ✅ Transient error 500 | ⬜ | Stripe retry logic |

---

## 🎯 Quick Test (Single Command)

Za brzo testiranje osnovnog flow-a:

```bash
# 1. Seed
psql -d vatevidence -c "INSERT INTO workspaces (id, name, created_at) VALUES ('11111111-1111-1111-1111-111111111111', 'Test', NOW()) ON CONFLICT DO NOTHING; INSERT INTO provider_connections (id, workspace_id, provider, mode, webhook_secret, created_at) VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 0, 0, 'whsec_test', NOW()) ON CONFLICT DO NOTHING;"

# 2. Test webhook
curl -X POST "https://localhost:5001/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111" \
  -H "Content-Type: application/json" \
  -H "Stripe-Signature: t=1,v1=dummy" \
  -d '{"id":"evt_quick_test","type":"payment_intent.succeeded","created":1640000000,"data":{"object":{"id":"pi_quick","amount":1000,"currency":"eur","created":1640000000}}}'

# 3. Verify
psql -d vatevidence -c "SELECT sequence, evidence_type FROM evidence_records WHERE transaction_id = (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_quick') ORDER BY sequence;"
```

**Očekivano**: 2 reda sa sequence 1,2

---

Ako svi testovi prolaze ✅, patch je **production-ready**! 🚀
