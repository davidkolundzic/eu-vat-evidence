

### Add initial migration for VatEvidence.Infrastructure project
```bash
dotnet ef migrations add <MigrationName> --project VatEvidence.Infrastructure\VatEvidence.Infrastructure.csproj --startup-project VatEvidence.Web\VatEvidence.Web.csproj
```

---


### Update database to latest migration for VatEvidence.Infrastructure project
```bash
dotnet ef database update --project VatEvidence.Infrastructure\VatEvidence.Infrastructure.csproj --startup-project VatEvidence.Web\VatEvidence.Web.csproj
```


### Kako provjeriti dali je na tablic primjenjeno ogranicenje nam UPDATE I DELETE
```sql

-- provjeri trigger-e
-- \dS evidence_records

-- ili listaj trigger-e
SELECT tgname
FROM pg_trigger
WHERE tgrelid = 'evidence_records'::regclass
  AND NOT tgisinternal;

```