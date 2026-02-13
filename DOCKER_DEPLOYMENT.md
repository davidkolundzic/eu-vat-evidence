# Docker Deployment na Render - Uputstvo

## 📦 Šta je kreirano

- ✅ **Dockerfile** - Multi-stage Docker build (optimizovan za produkciju)
- ✅ **.dockerignore** - Excludes nepotrebne fajlove
- ✅ **render.yaml** - Blueprint za automatski Render setup
- ✅ **HealthController.cs** - Health check endpoint (`/health`)
- ✅ **GitHub Actions workflow** - Opciono za CI/CD

---

## 🚀 Setup na Render (preko GitHub-a)

### 1. **Push na GitHub**
```bash
git add .
git commit -m "Add Docker support for Render deployment"
git push origin develop
```

### 2. **Konektuj Render sa GitHub repozitorijumom**

1. Idi na [Render Dashboard](https://dashboard.render.com/)
2. Klikni **New** → **Blueprint**
3. Izaberi svoj GitHub repo: `davidkolundzic/VatEvidenceSaaS`
4. Branch: `develop` (ili `main`)
5. Render će automatski detektovati `render.yaml`

**Render će automatski kreirati:**
- Web Service (Docker container)
- PostgreSQL Database
- Environment variables
- Automatic deployments na svaki push

### 3. **Podesi Environment Variables (Render će automatski podesiti, ali proveri)**

Web Service env vars:
- `ASPNETCORE_ENVIRONMENT` = `Production`
- `ConnectionStrings__Default` = auto-povezano sa DB
- `ASPNETCORE_URLS` = `http://+:8080`

Dodatne varijable koje možeš dodati:
- Stripe API keys (ako ih imaš)
- Logging nivoi
- Ostale secrets

---

## 🧪 Testiranje lokalno (opciono)

### Build Docker image:
```bash
docker build -t vatevidence-web .
```

### Run lokalno:
```bash
docker run -p 8080:8080 \
  -e ConnectionStrings__Default="Host=localhost;Database=vatevidence;Username=postgres;Password=password" \
  vatevidence-web
```

### Proveri health endpoint:
```bash
curl http://localhost:8080/health
```

---

## 📝 Render Blueprint Opcije (`render.yaml`)

Možeš promeniti:

### Region:
- `frankfurt` (EU)
- `oregon` (US West)
- `singapore` (Asia)

### Plan:
- `starter` (free)
- `standard` ($7/month)
- `pro` ($25/month)

### Branch:
- `main` - produkcija
- `develop` - staging

---

## 🔒 Security Best Practices

1. **Environment Variables** - Nikad ne hard-code secrets u kod
2. **Non-root user** - Docker container radi kao `appuser` (već podešeno)
3. **Health checks** - `/health` endpoint omogućava monitoring
4. **Rate limiting** - Već podešen u `Program.cs` za webhooks

---

## 📊 Monitoring

Render automatski pruža:
- Logs (real-time)
- Metrics (CPU, memory, requests)
- Health checks
- Automatic restarts

Pristup logovima:
1. Render Dashboard → Tvoj web service
2. **Logs** tab

---

## 🔄 Automatic Deployments

Render će automatski deployati kada:
- Push-uješ na `develop` branch (ili koji god si izabrao)
- GitHub Actions workflow se pokreće (opciono)

**Rollback:**
- Render Dashboard → Web Service → **Rollback** button

---

## 🐛 Troubleshooting

### Problem: Container se ne pokreće
**Check:**
```bash
# Proveri logs na Render Dashboard
# Ili build lokalno:
docker build -t test .
docker run test
```

### Problem: Database connection fails
**Check:**
- Environment variable `ConnectionStrings__Default` je ispravno podešena
- PostgreSQL service je running
- Network connectivity između servisa

### Problem: Health check fails
**Test lokalno:**
```bash
curl http://localhost:8080/health
```

---

## 📚 Resources

- [Render Docker Docs](https://render.com/docs/docker)
- [Render Blueprint Spec](https://render.com/docs/blueprint-spec)
- [ASP.NET Core Docker](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/)

---

## ✅ Checklist Pre-Deploya

- [ ] Push svih fajlova na GitHub
- [ ] Konektuj Render sa repom
- [ ] Proveri da render.yaml ima ispravne settinge
- [ ] Dodaj Stripe API keys u env vars (ako treba)
- [ ] Testiraj health endpoint posle deploya
- [ ] Podesi webhook URLs u Stripe Dashboard
- [ ] Proveri database migracije

---

**Happy deploying!** 🎉
