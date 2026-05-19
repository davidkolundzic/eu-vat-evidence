-- ================================================================================
-- VAT EVIDENCE VERIFICATION QUERY
-- ================================================================================
-- Purpose: Verify that IP evidence comes from buyer request, billing from webhook
-- Expected: 
--   - 1 row with evidence_type = 'Ipcountry', source_ref = 'cf-ipcountry' (from checkout)
--   - 1 row with evidence_type = 'Billingcountry', source_ref = 'stripe:charge:...' (from webhook)
-- ================================================================================

SELECT 
  t.provider_transaction_id AS payment_intent_id,
  t.provider_charge_id AS charge_id,
  t.amount_minor,
  t.currency,
  t.status,
  t.status_reason,
  e.evidence_type,
  e.country_code,
  e.source_ref,
  e.sequence,
  e.captured_utc,
  e.created_utc
FROM evidence_records e
JOIN transactions t ON t.id = e.transaction_id
WHERE t.provider_transaction_id = 'pi_3QqZJVHsOh5cz8fV0sqn6vTT' -- REPLACE WITH YOUR PAYMENT INTENT ID
ORDER BY e.sequence;

-- ================================================================================
-- EXPECTED OUTPUT (after full flow: checkout -> payment -> webhook):
-- ================================================================================
-- payment_intent_id              | charge_id             | status | evidence_type     | country_code | source_ref                        | sequence
-- pi_3QqZJVHsOh5cz8fV0sqn6vTT    | NULL                  | Ok     | Ipcountry         | HR           | cf-ipcountry                      | 1
-- pi_3QqZJVHsOh5cz8fV0sqn6vTT    | ch_3QqZ...            | Ok     | Billingcountry    | HR           | stripe:charge:ch_3QqZ...:billing  | 2
-- ================================================================================

-- ================================================================================
-- ADDITIONAL CHECKS
-- ================================================================================

-- 1) Verify IP evidence is NEVER created by webhooks (source_ref must be 'cf-ipcountry', never webhook-related)
SELECT 
  COUNT(*) AS bad_ip_evidence_count
FROM evidence_records
WHERE evidence_type = 'Ipcountry' 
  AND source_ref != 'cf-ipcountry';
-- Expected: 0

-- 2) Verify billing evidence is ONLY created from Stripe charge (webhook canonical fetch)
SELECT 
  COUNT(*) AS bad_billing_evidence_count
FROM evidence_records
WHERE evidence_type = 'Billingcountry' 
  AND NOT source_ref LIKE 'stripe:charge:%:billing';
-- Expected: 0

-- 3) Check transaction status distribution
SELECT 
  status,
  COUNT(*) AS count
FROM transactions
WHERE provider = 'Stripe'
GROUP BY status;
-- Expected: Ok (matching countries), Mismatch (different countries), Insufficient (missing evidence)
