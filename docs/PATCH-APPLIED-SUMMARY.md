# Patch Successfully Applied ?

## Summary

The three-part patch has been successfully applied to improve unique violation handling, controller retry logic, and add DB-level append-only enforcement for the evidence chain.

---

## Changes Applied

### 1. Robust Unique Violation Handling (PostgreSQL-specific)

**File**: `VatEvidence.Application/Webhooks/StripeWebhookProcessor.cs`

- ? Added `using Npgsql;` and `using System.Data.Common;`
- ? Replaced string-based exception detection with proper `PostgresException.SqlState == PostgresErrorCodes.UniqueViolation`
- ? Check constraint name explicitly: `ix_provider_events_workspace_id_provider_mode_provider_event_id`
- ? No more fragile string contains checks

**Before**:
```csharp
catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ix_provider_events_workspace_id_provider_mode_provider_event_id") == true)
```

**After**:
```csharp
catch (DbUpdateException ex)
  when (ex.InnerException is PostgresException pex
        && pex.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            pex.ConstraintName,
            "ix_provider_events_workspace_id_provider_mode_provider_event_id",
            StringComparison.Ordinal))
```

---

### 2. Smart Retry Logic (No More String Guessing)

**File**: `VatEvidence.Application/Webhooks/IWebhookProcessor.cs`

- ? Added `bool Retryable` parameter to `WebhookProcessResult` record

**File**: `VatEvidence.Application/Webhooks/StripeWebhookProcessor.cs`

- ? Added `IsRetryable(Exception ex)` method
  - Returns `true` for: `TimeoutException`, `DbException`, deadlocks, serialization failures, connection issues
  - Returns `false` for: validation errors, business logic errors
- ? All `WebhookProcessResult` returns now include the `Retryable` flag

**File**: `VatEvidence.Web/Controllers/StripeWebhookController.cs`

- ? Removed `IsTransientDatabaseError(string?)` method (was guessing based on text)
- ? Controller now uses `result.Retryable` flag to decide HTTP status:
  - `Retryable == true` ? HTTP 500 (Stripe will retry)
  - `Retryable == false` ? HTTP 200 (no retry needed)

---

### 3. DB Append-Only Enforcement (PostgreSQL Trigger)

**File**: `VatEvidence.Infrastructure/Migrations/20260202200000_AddEvidenceSequenceAndIdempotency.cs`

- ? Added PostgreSQL trigger function `evidence_records_append_only()`
- ? Trigger fires on `UPDATE` or `DELETE` attempts on `evidence_records` table
- ? Raises exception: `'evidence_records is append-only'`
- ? Added corresponding `DROP TRIGGER` and `DROP FUNCTION` in `Down()` migration

**Migration SQL**:
```sql
CREATE OR REPLACE FUNCTION evidence_records_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  RAISE EXCEPTION 'evidence_records is append-only';
END;
$$;

DROP TRIGGER IF EXISTS tr_evidence_records_append_only ON evidence_records;
CREATE TRIGGER tr_evidence_records_append_only
BEFORE UPDATE OR DELETE ON evidence_records
FOR EACH ROW
EXECUTE FUNCTION evidence_records_append_only();
```

---

### 4. Package Reference Added

**File**: `VatEvidence.Application/VatEvidence.Application.csproj`

- ? Added `<PackageReference Include="Npgsql" Version="10.0.0" />`
- Provides access to `PostgresException` and `PostgresErrorCodes`

---

## Build Status

? **Build successful**

All compilation errors resolved. The solution builds cleanly with the new changes.

---

## What This Fixes

### Before Patch (Problems):

? Unique violations detected via fragile string matching  
? Controller guessed transient errors from text (unreliable)  
? Stripe would retry ALL errors, even permanent ones  
? No DB-level protection against evidence tampering (UPDATE/DELETE)

### After Patch (Fixed):

? Unique violations detected via proper PostgreSQL error codes  
? Processor explicitly flags transient vs. permanent errors  
? Stripe retries ONLY transient errors (deadlocks, timeouts, connection issues)  
? DB trigger prevents any UPDATE/DELETE on evidence_records (immutable audit log)

---

## Migration Deployment

To apply the new trigger to your database:

```bash
dotnet ef database update --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
```

**Note**: The migration `20260202200000_AddEvidenceSequenceAndIdempotency` now includes both:
- The `sequence` column and indexes (already documented)
- The new append-only trigger (from this patch)

---

## Testing Scenarios

### 1. Duplicate Webhook Event

**Scenario**: Stripe sends same event twice (parallel or retry)

**Expected Behavior**:
- First attempt: Creates event + transaction + evidence
- Second attempt: Catches `PostgresException` with `UniqueViolation`, loads existing event, returns 200
- ? No string matching fragility

### 2. Transient DB Error (Deadlock)

**Scenario**: Database deadlock during event processing

**Expected Behavior**:
- `IsRetryable(ex)` returns `true` (deadlock detected via `PostgresErrorCodes.DeadlockDetected`)
- Controller returns HTTP 500
- Stripe retries after exponential backoff
- ? No string guessing

### 3. Permanent Error (Validation Failure)

**Scenario**: Invalid payload or business logic error

**Expected Behavior**:
- `IsRetryable(ex)` returns `false` (not a DB/timeout exception)
- Controller returns HTTP 200 (with `retryable: false` in body)
- Stripe does NOT retry
- ? Prevents infinite retry loops

### 4. Attempted Evidence Tampering

**Scenario**: Direct SQL `UPDATE` or `DELETE` on `evidence_records`

**Expected Behavior**:
```sql
UPDATE evidence_records SET country_code = 'XX' WHERE id = '...';
```

**Result**:
```
ERROR: evidence_records is append-only
```

? Immutable audit log enforced at DB level

---

## Rollback Plan

If you need to revert this patch:

1. **Migration rollback** (removes trigger):
   ```bash
   dotnet ef database update 20260129161251_InitialWithSnakeCase --project VatEvidence.Infrastructure --startup-project VatEvidence.Web
   ```

2. **Code rollback**:
   - Revert changes to `IWebhookProcessor.cs` (remove `Retryable` parameter)
   - Revert changes to `StripeWebhookProcessor.cs` (restore old catch blocks, remove `IsRetryable`)
   - Revert changes to `StripeWebhookController.cs` (restore `IsTransientDatabaseError`)
   - Remove `Npgsql` package reference from `VatEvidence.Application.csproj`

---

## Production Notes

1. **Npgsql Version**: Ensure `Npgsql 10.0.0` is compatible with your PostgreSQL server version
2. **Trigger Performance**: The trigger has minimal overhead (fires on UPDATE/DELETE, which should never happen)
3. **Retry Policy**: Stripe's default retry policy (3 attempts, exponential backoff) now works correctly with transient-only errors
4. **Monitoring**: Log when `result.Retryable == true` to track transient DB issues

---

**Status**: ? **Ready for testing and deployment**
