# Append-Only Enhancement: Restrict Delete on Transactions ?

## Summary

Added `OnDelete(DeleteBehavior.Restrict)` to the `EvidenceRecord ? Transaction` foreign key relationship. This prevents cascade deletion of transactions that have evidence records attached, ensuring audit log integrity.

---

## Changes Applied

### 1. EvidenceRecordConfig - Restrict Delete Behavior

**File**: `VatEvidence.Infrastructure/Persistence/Config/EvidenceRecordConfig.cs`

? Changed FK relationship from default (Cascade) to `Restrict`:

```csharp
b.HasOne(x => x.Transaction)
  .WithMany(x => x.EvidenceRecords)
  .HasForeignKey(x => x.TransactionId)
  // Append-only: evidence se nikad ne briše pojedina?no. Brisanje tx mora biti onemogu?eno.
  .OnDelete(DeleteBehavior.Restrict);
```

**Before**:
- Default cascade behavior (PostgreSQL: `ON DELETE CASCADE`)
- Deleting a `Transaction` would automatically delete all related `EvidenceRecord`s

**After**:
- `ON DELETE RESTRICT` constraint
- Attempting to delete a `Transaction` with evidence will fail with FK constraint violation
- Forces explicit handling of evidence before transaction cleanup

---

### 2. Database Migration

**File**: `VatEvidence.Infrastructure/Migrations/20260209132509_AddRestrictDeleteOnEvidenceRecords.cs`

? Created new migration that:
1. Drops existing FK constraint (`fk_evidence_records_transactions_transaction_id`)
2. Recreates FK with `onDelete: ReferentialAction.Restrict`

**Migration SQL (Up)**:
```sql
ALTER TABLE evidence_records
  DROP CONSTRAINT fk_evidence_records_transactions_transaction_id;

ALTER TABLE evidence_records
  ADD CONSTRAINT fk_evidence_records_transactions_transaction_id
  FOREIGN KEY (transaction_id)
  REFERENCES transactions(id)
  ON DELETE RESTRICT;
```

**Migration SQL (Down)** (rollback):
```sql
ALTER TABLE evidence_records
  DROP CONSTRAINT fk_evidence_records_transactions_transaction_id;

ALTER TABLE evidence_records
  ADD CONSTRAINT fk_evidence_records_transactions_transaction_id
  FOREIGN KEY (transaction_id)
  REFERENCES transactions(id)
  ON DELETE CASCADE;
```

---

## Build Status

? **Build successful**

---

## What This Fixes

### Append-Only Enforcement Layers

Now we have **three layers** of append-only protection:

1. ? **Application Layer**: `EvidenceAppendService` with idempotency checks
2. ? **DB Trigger**: `tr_evidence_records_append_only` prevents UPDATE/DELETE on evidence_records
3. ? **FK Constraint**: `ON DELETE RESTRICT` prevents cascade deletion from transactions

### Before This Patch:

? Deleting a `Transaction` would cascade-delete all `EvidenceRecord`s  
? No database-level protection against transaction deletion with evidence  
? Audit trail could be lost accidentally

### After This Patch:

? Cannot delete `Transaction` if it has any `EvidenceRecord`s (FK violation)  
? Forces explicit cleanup logic if transactions need archival/deletion  
? Audit trail is protected at DB level

---

## Migration Deployment

To apply the FK constraint change:

```bash
dotnet ef database update --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

**Note**: If your database has existing transactions with evidence, the migration will succeed without issues. The constraint prevents **future** deletions, not existing data.

---

## Testing Scenarios

### 1. Attempt to Delete Transaction with Evidence

**Test SQL**:
```sql
-- Create test transaction
INSERT INTO transactions (id, workspace_id, provider, mode, provider_transaction_id, amount_minor, currency, created_utc, status, status_reason)
VALUES (gen_random_uuid(), '11111111-1111-1111-1111-111111111111', 1, 1, 'pi_test_delete', 5000, 'EUR', NOW(), 0, 'Test');

-- Add evidence
INSERT INTO evidence_records (id, transaction_id, sequence, evidence_type, country_code, source_ref, captured_utc, record_hash)
VALUES (gen_random_uuid(), (SELECT id FROM transactions WHERE provider_transaction_id = 'pi_test_delete'), 1, 1, 'US', 'evt_test', NOW(), 'dummy_hash');

-- Try to delete transaction (should FAIL)
DELETE FROM transactions WHERE provider_transaction_id = 'pi_test_delete';
```

**Expected Result**:
```
ERROR: update or delete on table "transactions" violates foreign key constraint "fk_evidence_records_transactions_transaction_id" on table "evidence_records"
DETAIL: Key (id)=(...) is still referenced from table "evidence_records".
```

? Transaction deletion prevented by FK constraint

### 2. Delete Transaction WITHOUT Evidence

**Test SQL**:
```sql
-- Create transaction without evidence
INSERT INTO transactions (id, workspace_id, provider, mode, provider_transaction_id, amount_minor, currency, created_utc, status, status_reason)
VALUES (gen_random_uuid(), '11111111-1111-1111-1111-111111111111', 1, 1, 'pi_test_no_evidence', 5000, 'EUR', NOW(), 0, 'Test');

-- Try to delete (should SUCCEED)
DELETE FROM transactions WHERE provider_transaction_id = 'pi_test_no_evidence';
```

**Expected Result**:
```
DELETE 1
```

? Transactions without evidence can still be deleted (e.g., test data cleanup)

### 3. Application Code Attempting Delete

**C# Code**:
```csharp
var transaction = await _db.Transactions
  .Include(x => x.EvidenceRecords)
  .SingleAsync(x => x.Id == txId);

_db.Transactions.Remove(transaction);
await _db.SaveChangesAsync(); // Throws DbUpdateException
```

**Expected Exception**:
```
Microsoft.EntityFrameworkCore.DbUpdateException: 
An error occurred while saving the entity changes. 
See the inner exception for details.
  ---> Npgsql.PostgresException: 
  23503: update or delete on table "transactions" violates foreign key constraint ...
```

? Application code gets clear exception when attempting restricted delete

---

## Semantic Meaning

### Append-Only Audit Log Philosophy

The `ON DELETE RESTRICT` constraint enforces the semantic rule:

> **"Once evidence exists for a transaction, that transaction becomes part of the immutable audit log."**

This is the correct behavior for:
- ? **Tax compliance**: VAT evidence must be retained and cannot be deleted
- ? **Audit trails**: Tampering with evidence must be impossible
- ? **Legal requirements**: Financial records must be immutable

### Proper Cleanup Workflow

If you need to clean up test data or archive old transactions:

1. **First, explicitly handle evidence** (if allowed by business rules):
   ```sql
   -- Option A: Don't delete evidence (keep for compliance)
   -- Option B: Archive to separate table (if allowed)
   -- Option C: Truncate entire table (test/dev only)
   ```

2. **Then delete transaction**:
   ```sql
   DELETE FROM transactions WHERE ...;
   ```

3. **Or use database-level cascade** (only for non-production):
   ```sql
   -- Test/dev cleanup (bypasses constraint)
   TRUNCATE TABLE evidence_records, transactions CASCADE;
   ```

---

## Rollback Plan

If you need to revert to cascade delete behavior:

```bash
dotnet ef database update 20260202200000_AddEvidenceSequenceAndIdempotency --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

**Warning**: This will restore `ON DELETE CASCADE`, which is **NOT recommended for production** due to audit log integrity requirements.

---

## Production Notes

1. **Performance**: `ON DELETE RESTRICT` has **no performance impact** on normal operations (INSERTs, SELECTs)
2. **Compatibility**: Works with PostgreSQL 9.5+
3. **Test Cleanup**: In test environments, use `TRUNCATE ... CASCADE` for bulk cleanup
4. **Archival**: If you need transaction archival, implement explicit logic to move evidence first

---

## Complete Append-Only Stack

With all patches applied, evidence integrity is now protected by:

| Layer | Protection | File/Location |
|-------|-----------|--------------|
| **Application** | Idempotency checks, FOR UPDATE locks | `EvidenceAppendService.cs` |
| **DB Trigger** | Prevents UPDATE/DELETE on evidence_records | Migration `20260202200000` |
| **FK Constraint** | Prevents cascade delete from transactions | Migration `20260209132509` |
| **UNIQUE Indexes** | Prevents duplicate evidence | `ux_evidence_records_tx_type_source` |
| **Sequence** | Deterministic ordering | `sequence` column + index |

---

**Status**: ? **Build successful, ready for deployment**

**Next Step**: Run migration to apply FK constraint change
```bash
dotnet ef database update --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```
