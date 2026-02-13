# Stripe Webhook Integration - MVP Implementation

## 📋 Overview

This implementation provides a **secure, idempotent webhook endpoint** for Stripe events.

### ✨ Features Implemented:
- ✅ **Signature verification** (Stripe webhook secret)
- ✅ **Idempotency** (via `provider_events` unique constraint)
- ✅ **Event processing** (`payment_intent.succeeded` ➡️ Transaction creation)
- ✅ **Error logging** (to `ProviderEvent.Error` field)
- ✅ **Test & Live mode support**

---

## 🌐 Endpoint URLs

### Test Mode
```
POST https://your-domain.com/api/webhooks/stripe/test?workspace_id={GUID}
```

### Live Mode
```
POST https://your-domain.com/api/webhooks/stripe/live?workspace_id={GUID}
```

---

## 🔒 Security

### Stripe Signature Verification
The endpoint validates the `Stripe-Signature` header using the webhook secret from `ProviderConnection` table.

**Flow:**
1. Stripe sends event with `Stripe-Signature` header
2. Endpoint fetches webhook secret from DB (workspace + provider + mode)
3. Signature is validated using `Stripe.net` library
4. Invalid signatures return `401 Unauthorized`

---

## 🔄 Idempotency

Duplicate webhooks are handled automatically:

```sql
-- Unique constraint in provider_events table
CREATE UNIQUE INDEX ix_provider_events_idempotency 
ON provider_events (workspace_id, provider, mode, provider_event_id);
```

**Behavior:**
- First webhook ➡️ processes normally ➡️ returns `200 OK`
- Duplicate webhook ➡️ skips processing ➡️ returns `200 OK` with "Duplicate event"

---

## ⚙️ Event Processing

### Supported Events (MVP)

| Event Type | Action | Creates |
|-----------|--------|---------|
| `payment_intent.succeeded` | Extract payment details | `Transaction` record |

**Future events** (post-MVP):
- `charge.refunded`
- `payment_intent.payment_failed`

### Transaction Creation

Extracts from `payment_intent.succeeded`:
```csharp
- id                  ➡️ ProviderTransactionId (pi_...)
- amount              ➡️ AmountMinor (cents)
- currency            ➡️ Currency (EUR)
- latest_charge       ➡️ ProviderChargeId (ch_...)
- receipt_email       ➡️ CustomerEmail
- created             ➡️ CreatedUtc
```

**Initial status:** `TransactionStatus.Insufficient` (updated later by evidence evaluator)

---

## 🧪 Testing with Stripe CLI

### 1. Install Stripe CLI
```bash
stripe login
```

### 2. Forward webhooks to local endpoint
```bash
stripe listen --forward-to https://localhost:5001/api/webhooks/stripe/test?workspace_id=YOUR_WORKSPACE_GUID
```

### 3. Trigger test event
```bash
stripe trigger payment_intent.succeeded
```

### 4. Check logs
```bash
# Application logs
INFO: Successfully processed webhook evt_... for workspace ...

# Database
SELECT * FROM provider_events ORDER BY received_utc DESC LIMIT 5;
SELECT * FROM transactions ORDER BY created_utc DESC LIMIT 5;
```

---

## ⚙️ Configuration

### 1. Setup ProviderConnection

Before receiving webhooks, create a `ProviderConnection` record:

```sql
INSERT INTO provider_connections (id, workspace_id, provider, mode, webhook_secret, created_at)
VALUES (
  gen_random_uuid(),
  'YOUR_WORKSPACE_ID',
  1, -- Stripe
  1, -- Test mode
  'whsec_...', -- From Stripe Dashboard
  NOW()
);
```

### 2. Configure Stripe Dashboard

1. Go to **Developers → Webhooks**
2. Click **Add endpoint**
3. Enter URL:
   ```
   https://your-domain.com/api/webhooks/stripe/test?workspace_id=YOUR_WORKSPACE_GUID
   ```
4. Select event: `payment_intent.succeeded`
5. Copy **Signing secret** (`whsec_...`) to database

---

## ⚠️ Error Handling

### Logged Errors

Errors are captured in `provider_events.error` field:

```sql
SELECT id, type, processing_status, error 
FROM provider_events 
WHERE processing_status = 3; -- Failed
```

### Common Issues

| Error | Cause | Fix |
|-------|-------|-----|
| "Missing signature" | No `Stripe-Signature` header | Check Stripe webhook configuration |
| "Invalid signature" | Wrong webhook secret | Update `provider_connections.webhook_secret` |
| "Provider connection not found" | No DB record | Create `ProviderConnection` |
| "Duplicate event" | Webhook replay | Normal behavior (idempotent) |

---

## 📊 Monitoring Queries

### Recent webhooks
```sql
SELECT 
  pe.provider_event_id,
  pe.type,
  pe.processing_status,
  pe.received_utc,
  pe.error
FROM provider_events pe
WHERE workspace_id = 'YOUR_WORKSPACE_ID'
ORDER BY received_utc DESC
LIMIT 20;
```

### Transactions created today
```sql
SELECT 
  t.id,
  t.provider_transaction_id,
  t.amount_minor / 100.0 AS amount_eur,
  t.status,
  t.created_utc
FROM transactions t
WHERE workspace_id = 'YOUR_WORKSPACE_ID'
  AND created_utc >= CURRENT_DATE
ORDER BY created_utc DESC;
```

---

## 🚀 Next Steps (Post-MVP)

1. **Evidence extraction** (IP country, billing country, payment method)
2. **Transaction status evaluator** (OK/Mismatch/Insufficient)
3. **Background job processing** (Hangfire/Quartz)
4. **Webhook retry logic** (exponential backoff)
5. **Additional event types** (refunds, disputes)

---

## 📁 Code Structure

```
VatEvidence.Application/Webhooks/
  📂 Commands/
  │   └─ ProcessWebhookCommand.cs       # DTO for webhook data
  ├─ IWebhookProcessor.cs               # Processing interface
  ├─ StripeWebhookProcessor.cs          # Main processing logic
  ├─ IStripeSignatureValidator.cs       # Signature validation interface
  └─ StripeSignatureValidator.cs        # Stripe signature verification

VatEvidence.Web/Controllers/
  └─ StripeWebhookController.cs         # HTTP endpoint
```

---

## ✅ MVP Checklist

- [x] Webhook endpoint (test + live)
- [x] Signature verification
- [x] Idempotency handling
- [x] `payment_intent.succeeded` processing
- [x] Transaction creation
- [x] Error logging
- [ ] Evidence extraction (next feature)
- [ ] Transaction status evaluation (next feature)
