# Testing IP Evidence Refactor

## Quick Start

### 1. Start Application
```bash
cd VatEvidence.Web
dotnet run
```

### 2. Open Test Page
```
http://localhost:5000/checkout-test.html
```

### 3. Configure Workspace
- Get workspace ID from your database:
```sql
SELECT id, name FROM workspaces LIMIT 1;
```

- Ensure you have a ProviderConnection configured:
```sql
SELECT workspace_id, provider, mode, webhook_secret 
FROM provider_connections 
WHERE provider = 'Stripe';
```

### 4. Test Flow

#### A. Create Checkout Session (Buyer Request)
1. Fill form in `checkout-test.html`:
   - Workspace ID: `<your-workspace-guid>`
   - Mode: `test`
   - Amount: `1000` (10.00 EUR)
   - Email: `buyer@example.com`

2. Click "Create Checkout Session"
   - **IP evidence created HERE** from CF-IPCountry header
   - Transaction created with status `Insufficient`

3. You'll be redirected to Stripe Checkout
   - Use test card: `4242 4242 4242 4242`
   - Any future expiry, any CVC

#### B. Payment Triggers Webhook (Stripe Server Request)
4. After payment, Stripe sends webhook to:
   ```
   POST /api/webhooks/stripe/test?workspace_id={workspace-id}
   ```

5. Webhook processor:
   - Fetches canonical PaymentIntent state
   - **Billing evidence created HERE** from Charge.BillingDetails
   - Status evaluated: `Ok` / `Mismatch` / `Insufficient`

---

## Verification

### SQL Query
```sql
SELECT 
  t.provider_transaction_id AS pi_id,
  t.status,
  t.status_reason,
  e.evidence_type,
  e.country_code,
  e.source_ref,
  e.sequence,
  e.captured_utc
FROM evidence_records e
JOIN transactions t ON t.id = e.transaction_id
WHERE t.workspace_id = '<your-workspace-guid>'
ORDER BY t.created_utc DESC, e.sequence
LIMIT 10;
```

### Expected Results
For a successful flow with matching countries (e.g., HR):

| pi_id      | status | evidence_type     | country_code | source_ref                   | sequence |
|------------|--------|-------------------|--------------|------------------------------|----------|
| pi_3Qq...  | Ok     | Ipcountry         | HR           | cf-ipcountry                 | 1        |
| pi_3Qq...  | Ok     | Billingcountry    | HR           | stripe:charge:ch_...:billing | 2        |

---

## Local Development (Without Cloudflare)

If testing locally without Cloudflare proxy, you can simulate CF-IPCountry:

### Option 1: Modify fetch() in checkout-test.html
```javascript
const response = await fetch('/api/stripe/checkout/session', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'CF-IPCountry': 'HR' // SIMULATE CLOUDFLARE HEADER
  },
  body: JSON.stringify({...})
});
```

### Option 2: Use curl
```bash
curl -X POST http://localhost:5000/api/stripe/checkout/session \
  -H "Content-Type: application/json" \
  -H "CF-IPCountry: HR" \
  -d '{
    "workspaceId": "00000000-0000-0000-0000-000000000001",
    "mode": "test",
    "amountMinor": 1000,
    "currency": "EUR",
    "productName": "Test Product",
    "customerEmail": "buyer@example.com"
  }'
```

---

## Debugging

### 1. Check Logs
```bash
# IP evidence capture
grep "IP country captured" logs/app.log

# Webhook processing
grep "Canonical fetch for PI" logs/app.log

# Status evaluation
grep "Evidence OK" logs/app.log
```

### 2. Common Issues

**Issue:** "IP country not available"
- **Cause:** CF-IPCountry header missing
- **Fix:** Add header manually (see above) or deploy behind Cloudflare

**Issue:** "Webhook signature invalid"
- **Cause:** webhook_secret mismatch
- **Fix:** Update ProviderConnection.WebhookSecret with value from Stripe Dashboard

**Issue:** Status = "Insufficient"
- **Cause:** Missing billing or IP evidence
- **Fix:** Check evidence_records table, ensure both Ipcountry and Billingcountry exist

---

## Integration with Stripe CLI (Local Webhook Testing)

### 1. Install Stripe CLI
```bash
stripe listen --forward-to localhost:5000/api/webhooks/stripe/test?workspace_id=<your-workspace-guid>
```

### 2. Get Webhook Secret
```bash
stripe listen
# Copy webhook signing secret (whsec_...)
```

### 3. Update Database
```sql
UPDATE provider_connections 
SET webhook_secret = 'whsec_...' 
WHERE workspace_id = '<your-workspace-guid>' 
  AND provider = 'Stripe' 
  AND mode = 'Test';
```

### 4. Trigger Test Event
```bash
stripe trigger payment_intent.succeeded
```

---

## Production Deployment

### 1. Cloudflare Configuration
Ensure Cloudflare proxy is enabled (orange cloud) so `CF-IPCountry` header is available.

### 2. Webhook Endpoint
Configure in Stripe Dashboard:
```
https://yourdomain.com/api/webhooks/stripe/live?workspace_id={workspace-id}
```

### 3. Monitor Evidence Creation
```sql
-- Check hourly evidence creation rate
SELECT 
  DATE_TRUNC('hour', created_utc) AS hour,
  evidence_type,
  COUNT(*) AS count
FROM evidence_records
WHERE created_utc > NOW() - INTERVAL '24 hours'
GROUP BY hour, evidence_type
ORDER BY hour DESC;
```

---

## Acceptance Criteria

- [x] IP evidence created ONLY from buyer checkout requests
- [x] Webhook creates ONLY billing evidence
- [x] Status evaluation works correctly (Ok/Mismatch/Insufficient)
- [x] Idempotency preserved (duplicate webhooks don't create duplicate evidence)
- [x] DB transactions ensure evidence chain integrity
- [x] Test page works end-to-end

**Refactor complete. System ready for production.**
