# IP Country Hint Extraction - Developer Guide

## Overview

`StripeWebhookController` sada koristi **clean helper metodu** `GetIpCountryHint()` koja izvlači IP country hint iz različitih izvora sa definisanom prioritetom.

---

## Extraction Priority

### 1. **CF-IPCountry** (Primary) ✅
**Izvor:** Cloudflare GeoIP header  
**Okruženja:** Production, Staging  
**Primer:**
```http
CF-IPCountry: HR
```

**Kada radi:**
- Cloudflare proxy je uključen (🟠 orange cloud u DNS settings)
- Request prolazi kroz Cloudflare CDN

---

### 2. **X-IP-Country** (Fallback) ⚠️
**Izvor:** Generic proxy/CDN header  
**Okruženja:** Production, Staging (ako Cloudflare nije dostupan)  
**Primer:**
```http
X-IP-Country: HR
```

**Kada radi:**
- Lokalni razvoj sa custom proxy-em
- Alternative CDN koji šalje ovaj header

---

### 3. **X-Debug-IPCountry** (Debug header) 🔧
**Izvor:** Manual override za testiranje  
**Okruženja:** **SAMO Staging**  
**Primer:**
```http
X-Debug-IPCountry: RS
```

**Kada radi:**
- `ASPNETCORE_ENVIRONMENT=Staging`
- Korisno za E2E testiranje bez Cloudflare-a
- **Ignoriše se u Production okruženju!**

---

### 4. **ip_country query parameter** 🔧
**Izvor:** Manual override preko URL-a  
**Okruženja:** **SAMO Staging**  
**Primer:**
```
POST /api/webhooks/stripe/test?workspace_id=xxx&ip_country=BA
```

**Kada radi:**
- `ASPNETCORE_ENVIRONMENT=Staging`
- Korisno za testiranje sa Stripe CLI-em
- **Ignoriše se u Production okruženju!**

---

## Implementacija

### Helper metoda: `GetIpCountryHint()`

```csharp
private string? GetIpCountryHint()
{
  // 1) Primary: Cloudflare GeoIP header
  if (Request.Headers.TryGetValue("CF-IPCountry", out var cf) && !StringValues.IsNullOrEmpty(cf))
  {
    var v = cf.ToString().Trim().ToUpperInvariant();
    return string.IsNullOrWhiteSpace(v) ? null : v;
  }

  // 2) Fallback: generic proxy/CDN header
  if (Request.Headers.TryGetValue("X-IP-Country", out var xip) && !StringValues.IsNullOrEmpty(xip))
  {
    var v = xip.ToString().Trim().ToUpperInvariant();
    return string.IsNullOrWhiteSpace(v) ? null : v;
  }

  // 3) Staging-only override (for E2E testing without Cloudflare)
  if (_env.IsStaging())
  {
    // Debug header override
    if (Request.Headers.TryGetValue("X-Debug-IPCountry", out var dbgH) && !StringValues.IsNullOrEmpty(dbgH))
    {
      var v = dbgH.ToString().Trim().ToUpperInvariant();
      return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // Query parameter override (useful for Stripe CLI testing)
    if (Request.Query.TryGetValue("ip_country", out var dbgQ) && !StringValues.IsNullOrEmpty(dbgQ))
    {
      var v = dbgQ.ToString().Trim().ToUpperInvariant();
      return string.IsNullOrWhiteSpace(v) ? null : v;
    }
  }

  return null;
}
```

---

## Testiranje

### **Production (Cloudflare)**
```http
POST https://api.vatevidence.info/api/webhooks/stripe/live?workspace_id=xxx
CF-IPCountry: HR
X-Debug-IPCountry: RS  # ❌ IGNORED in Production
```
**Rezultat:** `ipCountryHint = "HR"` (samo CF-IPCountry se koristi)

---

### **Staging (sa Cloudflare)**
```http
POST https://staging-api.vatevidence.info/api/webhooks/stripe/test?workspace_id=xxx
CF-IPCountry: HR
```
**Rezultat:** `ipCountryHint = "HR"`

---

### **Staging (bez Cloudflare, debug header)**
```http
POST https://staging-api.vatevidence.info/api/webhooks/stripe/test?workspace_id=xxx
X-Debug-IPCountry: RS
```
**Rezultat:** `ipCountryHint = "RS"` ✅

---

### **Staging (Stripe CLI test, query param)**
```bash
stripe trigger payment_intent.succeeded --add payment_intent:metadata[ip_country]=BA
```
ili:
```http
POST https://staging-api.vatevidence.info/api/webhooks/stripe/test?workspace_id=xxx&ip_country=BA
```
**Rezultat:** `ipCountryHint = "BA"` ✅

---

### **Lokalni development (bez headera)**
```http
POST http://localhost:5000/api/webhooks/stripe/test?workspace_id=xxx
```
**Rezultat:** `ipCountryHint = null` (nema CF headera niti debug overridea)

---

## Verifikacija

### 1. Proveri da li Cloudflare šalje header:
```http
GET https://staging-api.vatevidence.info/api/health/headers
```

Očekivani response:
```json
{
  "cloudflare": {
    "enabled": true,
    "ipCountry": "HR"
  }
}
```

### 2. Proveri audit trail u DB-u:
```sql
SELECT 
  t.provider_transaction_id,
  e.evidence_type,
  e.country_code,
  e.source_ref,
  e.value_raw
FROM evidence_records e
JOIN transactions t ON t.id = e.transaction_id
WHERE t.provider_transaction_id = 'pi_xxx';
```

**Očekivani rezultat:**
```
| evidence_type | country_code | source_ref    | value_raw                                       |
|---------------|--------------|---------------|-------------------------------------------------|
| Ipcountry     | HR           | cf-ipcountry  | {"country":"HR","source":"CF-IPCountry",...}   |
```

---

## Security Notes

### ⚠️ **Debug overrides su DISABLED u Production**
```csharp
if (_env.IsStaging()) // ✅ Debug headers rade SAMO u Staging
{
  // X-Debug-IPCountry override
  // ip_country query param override
}
```

**Zašto?**
- Sprečava malicious header injection u production
- Onemogućava fake IP country u audit trail-u
- Debug opcije su dostupne samo u kontrolisanom staging okruženju

### ✅ **Cloudflare header je siguran**
- `CF-IPCountry` se **NE MOŽE** lažirati od strane korisnika
- Cloudflare ga postavlja na edge serveru pre nego što request stigne do origin-a
- Ako korisnik pokuša da šalje custom `CF-IPCountry`, Cloudflare će ga overwrite-ovati

---

## Troubleshooting

### **Problem:** `ipCountryHint = null` u staging-u
**Rešenje:**
1. Proveri da li je Cloudflare proxy uključen: 🟠 orange cloud u DNS settings
2. Koristi debug override: `X-Debug-IPCountry: HR` header ili `?ip_country=HR` query param
3. Proveri `/api/health/headers` endpoint da vidiš koje headere dobijate

### **Problem:** Debug override ne radi u production
**Očekivano ponašanje!** Debug overrides su namerno disabled u production. Koristi Cloudflare `CF-IPCountry` header.

### **Problem:** `CF-IPCountry` header je `"XX"` (nepoznata zemlja)
**Razlog:** Cloudflare ne može odrediti zemlju iz IP adrese (npr. VPN, Tor, data center IPs)  
**Rešenje:** Ovo je validno stanje, `"XX"` je legalan ISO kod za "unknown". Evidence append će failovati sa `IsValidCountryCode("XX") = false` (jer nije 2 slova).

---

## Best Practices

1. **Production:** Osloni se SAMO na `CF-IPCountry` header
2. **Staging:** Koristi debug overrides za E2E testove
3. **Lokalni dev:** Koristi Stripe CLI sa `--add metadata[ip_country]=HR` za simulaciju
4. **Monitoring:** Prati `evidence_records.source_ref` u DB-u da vidiš izvor podataka
5. **Audit:** `value_raw` JSON snapshot sadrži `"source": "CF-IPCountry"` za transparentnost

---

## Related Files

- **Controller:** `VatEvidence.Web\Controllers\StripeWebhookController.cs`
- **Processor:** `VatEvidence.Application\Webhooks\StripeWebhookProcessor.cs`
- **Test file:** `VatEvidence.Web\webhook-stripe-debug.http`
- **Health check:** `VatEvidence.Web\Controllers\HealthController.cs` (`/api/health/headers`)

---

## Questions?

- Proveri `REFACTOR_SUMMARY.md` za detaljan opis canonical pipeline-a
- Proveri `STRIPE_CONFIG.md` za Stripe API konfiguraciju
- Proveri `LEGACY_CODE_MIGRATION.md` za info o legacy code cleanup-u
