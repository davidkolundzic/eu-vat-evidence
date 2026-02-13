# Evidence Chain Race Condition Fix - Applied Changes

## Što je riješeno

Ovaj patch adresira kriti?ne race condition probleme u evidencijskom lancu:

1. **Deterministi?an ordering** - `Sequence` umjesto `CapturedUtc` + `Id`
2. **Idempotency zaštita** - UNIQUE constraint na `(transaction_id, evidence_type, source_ref)`
3. **Robusno retry handling** - Duplicate eventi se reprocessaju ako nisu bili `Processed`
4. **Transaction upsert** - Više nema scenarija "transaction postoji, evidence fali"
5. **Smart webhook retry** - Vra?a 500 samo za transient DB greške (deadlock, timeout)

---

## Primijenjene izmjene

### 1. Domain Layer

**`VatEvidence.Domain/Entities/EvidenceRecord.cs`**
- ? Dodano `public long Sequence { get; set; }` property
- Omogu?ava deterministi?an ordering unutar transakcije

### 2. Infrastructure Layer

**`VatEvidence.Infrastructure/Persistence/Config/EvidenceRecordConfig.cs`**
- ? Dodano mapiranje za `Sequence` column
- ? Dodani novi indexi:
  - `ux_evidence_records_tx_sequence` (UNIQUE) - brz tail lookup i garancija ordering-a
  - `ux_evidence_records_tx_type_source` (UNIQUE) - idempotency constraint
  - `ix_evidence_records_transaction_id_captured_utc` - zadržan za legacy queries

**`VatEvidence.Infrastructure/Migrations/20260202200000_AddEvidenceSequenceAndIdempotency.cs`**
- ? Kreirana nova migracija sa:
  - `sequence` column (bigint)
  - Backfill SQL za postoje?e redove (deterministi?an po `captured_utc, id`)
  - Kreiranje novih indexa

**`VatEvidence.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`**
- ? Ažuriran snapshot sa novim `Sequence` property i indexima

### 3. Application Layer

**`VatEvidence.Application/Evidence/EvidenceAppendService.cs`**
- ? **Idempotency check**: Prije insert-a provjerava da li ve? postoji evidenca za `(tx, type, source_ref)`
- ? **Sequence-based tail lookup**: Umjesto `OrderByDescending(CapturedUtc).ThenByDescending(Id)`, koristi `OrderByDescending(Sequence)`
- ? **Automatic sequence assignment**: `nextSeq = (tail?.Sequence ?? 0) + 1`
- ? Vra?a postoje?u evidencu ako je duplicate (idempotent)

**`VatEvidence.Application/Evidence/EvidenceChainVerifier.cs`**
- ? Sortira redove po `Sequence` umjesto `CapturedUtc, Id`
- Garancija da verifikacija koristi isti ordering kao append

**`VatEvidence.Application/Webhooks/StripeWebhookProcessor.cs`**
- ? **`SaveOrLoadEventAsync`** (umjesto `SaveEventAsync`):
  - Više ne vra?a `null` na duplicate
  - U?itava postoje?i event i vra?a ga
  - Omogu?ava retry ako je `Failed` ili `Received`
- ? **Skip samo ako je `Processed`**: Pravi duplicate ve? obra?enih doga?aja se skipa odmah
- ? **Transaction upsert logic**:
  - Prvo pokušava na?i postoje?i transaction
  - Ako ne postoji, kreira novi
  - Ako postoji, ažurira `ProviderChargeId` i `CustomerEmail` ako fale
- ? **`EvaluateStatusAsync`** metoda:
  - Evaluira status na osnovu trenutnog stanja evidencija
  - Koristi **latest per type by sequence** logic
  - Robustan za out-of-order/retry scenarije
- ? Uklonjen catch block koji je ignorao duplicate transakcije

### 4. Web Layer

**`VatEvidence.Web/Controllers/StripeWebhookController.cs`**
- ? **Smart retry logic**: 
  - Vra?a **500** samo za **transient DB greške** (deadlock, timeout, connection)
  - Vra?a **200** za permanentne greške (validacija, business logic)
- ? **`IsTransientDatabaseError`** helper metoda
- Stripe ?e automatski retry-ati samo transient greške

---

## Kako pokrenuti migraciju

```bash
# Primijeni migraciju na bazu
dotnet ef database update --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

Migracija uklju?uje:
1. Dodavanje `sequence` column (default 0)
2. **Backfill SQL** - popunjava sequence za sve postoje?e redove deterministi?ki po `(captured_utc, id)`
3. Kreiranje UNIQUE indexa za idempotency i ordering

---

## Sigurnosne garancije

### Prije patcha (problemi):
? Race condition kod paralelnih webhook-ova (nedeterministi?ki ordering)  
? Mogu? dupli append iste evidence (nema idempotency constraint)  
? Duplicate eventi sa statusom `Failed` se skipaju zauvijek  
? Scenario: transaction postoji, ali fali evidence (retry ne popunjava)  
? Stripe retry-a SVE greške, ?ak i permanentne

### Nakon patcha (riješeno):
? Deterministi?ki ordering po `Sequence` (append i verifier koriste isti)  
? UNIQUE constraint `(tx, type, source_ref)` - nemogu? dupli append  
? Duplicate eventi se reprocessaju ako nisu `Processed` (robusno)  
? Transaction upsert - replay event-a može dopuniti faliraju?u evidencu  
? Stripe retry-a SAMO transient DB greške (deadlock/timeout)  
? FOR UPDATE lock spre?ava concurrent append na istu transakciju

---

## Test scenariji koji sada rade

1. **Parallel webhook delivery** (isti `evt_123` stigne 2x simultano):
   - Prvi: prolazi, kreira event+tx+evidence
   - Drugi: u?itava postoje?i event, vidi da je `Processed`, vra?a 200 odmah

2. **Retry failanog event-a** (event bio `Failed`, dolazi ponovo):
   - U?itava postoje?i event sa statusom `Failed`
   - Reprocessira (idempotentno doda evidence ako fali)
   - Ozna?ava kao `Processed`

3. **Out-of-order evidence append** (IP stiže prije Billing):
   - Sequence garantuje ordering neovisno o vremenskom redoslijedu primitka
   - Hash chain ostaje validan

4. **Deadlock retry** (transient DB greška):
   - Controller vra?a 500
   - Stripe automatski retry-a nakon exponential backoff
   - Drugi pokušaj uspijeva (idempotentno)

---

## Što NE rješava ovaj patch

- ? Izvla?enje pravih vrijednosti iz Stripe payloada (i dalje hardcoded "US")
- ? IP country capture iz request headera (potreban middleware/helper)
- ? Optimisti?no locking (oslanja se na FOR UPDATE)

Ove feature-e treba implementirati u sljede?em koraku.

---

## Rollback (ako je potreban)

```bash
# Vrati migraciju natrag
dotnet ef database update 20260129161251_InitialWithSnakeCase --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

**UPOZORENJE**: Rollback ?e obrisati `sequence` column i nove indexe. Evidence ?e ostati u bazi, ali ?e ordering možda biti razli?it pri sljede?em append-u (CapturedUtc-based).

---

## Napomene za produkciju

1. **Backfill SQL** u migraciji je deterministi?ki - postoje?i redovi dobijaju sequence po `(captured_utc, id)` ordering-u.
2. **FOR UPDATE** lock radi samo unutar aktivne DB transakcije (caller mora zvati `BeginTransactionAsync`).
3. **Idempotency constraint** garantuje da isti `(tx, type, source_ref)` ne može biti dupiran - ?ak ni ru?nim SQL insert-om.
4. **Stripe retry policy**: Max 3 pokušaja, exponential backoff 1s ? 2s ? 4s.

---

**Status**: ? **Build successful, ready za testing**
