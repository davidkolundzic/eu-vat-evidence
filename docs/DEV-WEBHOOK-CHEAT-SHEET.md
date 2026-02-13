# 🧘 Kad se vratiš danas ili sutra:

🟢 ideš točno ovim redom

🟢 bez razmišljanja

🟢 bez panike
## 🧠 VAT SaaS – DEV WEBHOOK CHEAT-SHEET (copy/paste)

---

Ako je workspace obrisan
```sQL	
insert into workspaces (id, name, created_at)
values (
  '11111111-1111-1111-1111-111111111111',
  'Demo Workspace',
  now()
);

```
---

0️⃣ Reset (ako treba)

```SQL
delete from evidence_records;
delete from transactions;
delete from provider_events;
```
---

1️⃣ Start aplikacije
```bash
dotnet run
```

---

2️⃣  Stripe listen (UVIJEK NOVI whsec)

```bash
stripe listen --forward-to http://localhost:5152/api/webhooks/stripe/test?workspace_id=11111111-1111-1111-1111-111111111111
```
➡️ Kopiraj whsec_......

---

3️⃣ Upis webhook secreta u DB (OBAVEZNO)
```SQL
update provider_connections
set webhook_secret = 'whsec_67f57681b0c47a6cc2258787d7a53bfbad2a7b0dab093bd5e241c247a0e13168'
where workspace_id = '11111111-1111-1111-1111-111111111111'
  and provider = 1
  and mode = 1;
```
---

4️⃣ Trigger event
```
stripe trigger payment_intent.succeeded
```

5️⃣ Provjera (3 tablice)
```
select * from provider_events order by received_utc desc limit 3;
select * from transactions order by created_utc desc limit 3;
select * from evidence_records order by created_utc desc limit 3;

```
Ako vidis red u sve tri → pipeline radi ✅

---
## 🔒 Preporuka (2min posla, zlata vrijedno)
