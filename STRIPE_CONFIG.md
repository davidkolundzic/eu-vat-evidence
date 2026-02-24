# Stripe API Configuration

## Local Development (appsettings.Development.json)

```json
{
  "Stripe": {
    "TestSecretKey": "sk_test_...",
    "LiveSecretKey": "sk_live_..."
  }
}
```

## Production Environment Variables (Render)

Postavi sledeće environment varijable u Render dashboard-u:

```
Stripe__TestSecretKey=sk_test_...
Stripe__LiveSecretKey=sk_live_...
```

**Napomena:**
- `__` (dva underscore-a) se koristi za nested konfiguraciju u .NET
- Stripe API ključevi možeš dobiti iz: https://dashboard.stripe.com/apikeys
- **NIKADA** nemoj commit-ovati secret ključeve u Git repozitorijum
- Za lokalnu dev verziju koristi `appsettings.Development.json` (već je u .gitignore)

## Verifikacija konfiguracije

Nakon deploy-a, proveri health endpoint:

```bash
curl https://tvoja-app.onrender.com/api/health
```

Ako Stripe ključevi nisu konfigurisani, webhook processing će failovati sa:
```
Stripe API key not configured for mode: test/live
```

## Testiranje webhook-a

1. Koristi Stripe CLI za slanje test webhook-a:
```bash
stripe listen --forward-to http://localhost:5000/api/webhooks/stripe/test?workspace_id=YOUR_WORKSPACE_ID
```

2. Triggeruj test event:
```bash
stripe trigger payment_intent.succeeded
```

3. Proveri logove u aplikaciji da li se webhook procesira sa "Canonical fetch" logom.
