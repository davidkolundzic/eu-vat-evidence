# Contract Portfolio Handoff — David Kolundžić

**Purpose of this document**: complete context transfer to a new Claude/AI chat session so portfolio work for EU contract hunting can continue without losing prior strategic decisions. Self-contained — paste this into the new chat as the first message.

---

## Who I am

- Croatian Angular + .NET full-stack developer, Zagreb-based
- Targeting freelance/contract roles in **Germany and Netherlands**
- Languages: Croatian (native), English (fluent), German (basic, learning)
- Available May 2026, looking for 6-month contracts
- Target hourly rate: €85–120/h depending on niche fit
- Target work mode: hybrid (2–3 days on-site OK), pure remote preferred

---

## Strategic context (decided — do not re-debate)

I'm running a **dual-track strategy**:

1. **Primary income (next 6–18 months)**: EU contract roles in DE/NL fintech / enterprise Angular space
2. **Long-term business**: Rental SaaS for Adriatic small operators (separate work, separate chat)
3. **Bridge asset**: existing `VatEvidenceSaaS` (.NET) + `axiom-dashboard` (Angular 21) codebases positioned as **portfolio leverage** for contract rate negotiation

**Key strategic insight already validated**: my `VatEvidenceSaaS` codebase has rare audit-grade signals (hash-chained `EvidenceRecord`, canonical Stripe webhook pipeline, Clean Architecture) that DE/NL fintech recruiters recognize as senior-level. The narrative repositioning is from "VAT compliance MVP" to **"EU fintech compliance reference architecture with tamper-evident audit trail"**.

---

## EU contract market reality (April 2026, do not re-research)

**Realistic rates**:

| Segment | Germany (€/h) | Netherlands (€/h) |
|---|---|---|
| Mid (3–5y) Angular | 75–95 | 70–90 |
| Senior (5–8y) Angular | 95–120 | 85–110 |
| Senior + niche (NgRx, micro-frontends, perf, a11y, RxJS deep) | 110–140 | 100–125 |

**Niches that pay premium right now**:
- Angular + accessibility (WCAG 2.2, EN 301 549) — German public sector
- Legacy Angular migration (AngularJS → 18+, RxJS → Signals)
- Angular + micro-frontends (Module Federation / Native Federation) in banking/insurance
- Angular performance (LCP/INP optimization on enterprise apps)
- Angular + secure healthcare IT (gematik / TI-Messenger / KIM in DE)
- Angular + SAP Fiori / SAPUI5 hybrid migrations

**Hiring channels that work for DE/NL contracts** (not Toptal/Upwork):
- Specialist agencies DE: Hays, Michael Page Tech, Robert Walters, GULP, SOLCOM, Goetzfried, Etengo, Modis/Akkodis
- Specialist agencies NL: Yacht, Harvey Nash, Levy Professionals, Compagnon, Striive, Blue Lynx
- Platforms: freelancermap.de, freelance.de, Hays.de
- LinkedIn (DE/NL recruiter cold outreach with rate stated)

**Critical compliance constraints**:
- Germany: Scheinselbstständigkeit risk — multiple parallel clients preferred, AÜG agency framework safer for first-timers
- Netherlands: DBA enforcement active since Jan 2026, fines apply — IT is high-risk sector

---

## Existing code assets (do not rewrite, repurpose)

### `VatEvidenceSaaS/` — .NET backend, Clean Architecture

Located at `C:\Users\david\Documents\Solutions\VatEvidenceSaaS`

**What it is**: VAT evidence + Stripe webhook compliance backend with hash-chain audit trail.

**Structure**:
- `VatEvidence.Domain/Entities/` — Workspace, User, Transaction, EvidenceRecord (hash-chained), ProviderConnection, ProviderEvent, Export
- `VatEvidence.Domain/Enums.cs` — CountryClassification (EU/EEA/non-EU)
- `VatEvidence.Application/Stripe/` — CanonicalStripeSnapshot, StripeCanonicalReader (server-to-server fetch)
- `VatEvidence.Application/Webhooks/` — StripeWebhookProcessor, StripePayloadExtractor, StripeSignatureValidator
- `VatEvidence.Application/Evidence/` — EvidenceAppendService, EvidenceChainVerifier, EvidenceHashService
- `VatEvidence.Web/Controllers/` — StripeWebhookController, StripeCheckoutController, HealthController
- `VatEvidence.Web/Pages/Transactions/Verify.cshtml` — chain verification UI

**Recruiter-relevant signals (these are the gems)**:
1. **Hash-chained `EvidenceRecord`** with `RecordHash` + `PrevRecordHash` + monotonic `Sequence` — tamper-evident audit pattern, rare in portfolios
2. **Canonical Stripe pipeline** — webhook signature → server-to-server `PaymentIntentService.GetAsync(expand: latest_charge)` → upsert → append billing+IP evidence with `SourceRef` → evaluate status — production-grade, not toy
3. **Multi-tenancy** via `WorkspaceId` everywhere
4. **Multi-mode** (test/live) Stripe support
5. **EU/EEA country classification** — tax compliance domain depth

### `axiom-dashboard/` — Angular 21 frontend

Located at `C:\Users\david\Documents\webapps\axiom-dashboard`

**What it is**: Angular dashboard demonstrating scalable frontend architecture.

**Stack** (Angular 21, latest as of April 2026):
- Standalone components (no NgModules)
- Signals + computed (modern state pattern)
- `toSignal` + `rxResource` patterns in stores
- Vitest (not Jest) for testing
- MSW for API mocks
- Bootstrap 5 + Bootstrap Icons
- SCSS with design tokens, light/dark `ThemeService`

**Structure**:
- `src/app/core/` — services, theme
- `src/app/shared/` — UI primitives, ErrorFormatService
- `src/app/domain/` — typed models (Project, Workspace, SummaryBlock, etc.)
- `src/app/layout/` — DashboardShell, Sidebar, Topbar with theme toggle
- `src/app/features/` — dashboard-home, video-summary, settings, home
- `src/app/mocks/` — MSW handlers

**Current weakness**: branded as "AI video workspace" — needs reframing to "EU fintech compliance dashboard" to align with VatEvidenceSaaS narrative for recruiters.

---

## What needs to be done — concrete portfolio tasks

### Track 1: Repository polish & narrative

1. **Rebrand `axiom-dashboard` README** from "AI video workspace" to fintech compliance positioning. Hero line: *"Axiom — EU Fintech Compliance & Reconciliation Platform. Tamper-evident transaction evidence, Stripe payout reconciliation. Built with Angular 21 (signals, standalone) + .NET (Clean Architecture, hash-chain audit trail)."*
2. **Connect `axiom-dashboard` to `VatEvidenceSaaS` API** — expose JSON endpoints from `VatEvidence.Web` (currently Razor Pages), wire Angular signal stores to them.
3. **Build Evidence module** in axiom-dashboard: transactions list with hash-chain visualization on detail view + Verify button calling `EvidenceChainVerifier`.
4. **Build Stripe Payout Reconciliation module** (full design already exists — see `UNIFIED_STRATEGY.md` if generated, or ask for the design):
   - .NET endpoints in `Api/V1/PayoutsController.cs`
   - Angular store with rxResource + signals
   - Bootstrap-based list/detail/match-bank pages
   - Discrepancy banner showing payout amount mismatches
   - "View evidence" cross-link from each payout line to evidence chain
5. **README as case study** (1500–2500 words): problem statement, tech decisions log, mjerljivi rezultati (Lighthouse, bundle size, axe-core), trade-offs. Live demo link in top 3 lines.

### Track 2: Live deployment & CI

1. **Deploy `axiom-dashboard`** to Vercel/Cloudflare Pages (free tier). Live URL is non-negotiable for recruiters.
2. **Deploy `VatEvidence.Web`** to Render (free tier or $7/mo). Postgres on Render or Supabase.
3. **GitHub Actions CI**: lint, test, build, Lighthouse CI gates (LCP < 2.5s, INP < 200ms), deploy on push to main.
4. **Status badges** in README.
5. **axe-core a11y test** in CI — must pass WCAG 2.2 AA on all screens.

### Track 3: Performance & accessibility polish

1. Bundle size budget enforced in `angular.json` (already partially configured: 500kB warning, 1MB error).
2. Lighthouse CI thresholds.
3. Custom focus management on dialogs/modals.
4. Document a11y audit in `/docs/a11y-audit.md` with screenshots.

### Track 4: CV + LinkedIn + outreach

1. **CV rewrite** — one page, English. Top line: *"Senior Angular Contractor — Enterprise migration & micro-frontends, EU fintech compliance focus."* No filler.
2. **LinkedIn**:
   - Headline: "Senior Angular Contractor / Freelancer — Available May 2026"
   - Location: Berlin or Amsterdam (where you can actually be 2 days/week)
   - "Open to contract work" toggle ON
   - Hourly rate target stated in About section
3. **Cold outreach templates** — 5 versions tailored per agency type (Hays generalist vs. SOLCOM enterprise vs. Yacht NL specialist).
4. **LinkedIn content** — 1 post/week on niche Angular pattern (Module Federation gotcha, Signal migration, perf tip). German recruiters browse these.

### Track 5: Demo recording

1. **Loom 90-second demo** — recruiter-targeted, scripted:
   - Hero shot: payout list with discrepancy
   - Click into detail: evidence chain visualization
   - Tech stack callout: Angular 21 signals, .NET Clean Architecture, hash-chained audit
   - Closing: "Available May 2026, €X/h, DE/NL hybrid OK"

---

## Constraints & decisions already made

- **Do NOT** rewrite the `VatEvidence.Domain` hash-chain pattern — it's the differentiator
- **Do NOT** introduce new frontend libraries beyond Angular 21 + Bootstrap 5 (no Material, no PrimeNG)
- **Do NOT** introduce NgRx classic — stick with signal-based stores using `rxResource` + `computed`
- **Do NOT** invest in Peppol AP certification or any regulatory path that costs €5k+ — out of budget
- **Do** keep multi-tenancy (`WorkspaceId`) everywhere — recruiters notice
- **Do** keep WCAG 2.2 AA on every screen — cheap senior signal
- **Do** brand for fintech compliance, not "AI workspace"

---

## What the new chat should do first

Suggested opening prompt for the new chat (paste this after this whole document):

> *I want to work on my contract portfolio for EU (DE/NL) Angular contracting. The handoff document above has the full context. Please:*
> 
> *1. Confirm you've read and understood the context (1 paragraph max).*
> *2. Suggest a prioritized order of execution across the 5 tracks above (CV polish, repo polish, deploy, demo, outreach) given that I want first recruiter screen booked within 14 days.*
> *3. Ask me 3 clarifying questions before suggesting any code changes — specifically about my available time per day, current LinkedIn state, and whether I have prior contract references to include.*

---

## Useful prior decisions (reference if needed)

- **Project lead time analysis**: contract roles in DE/NL hire 4–8 weeks from first agency contact to start date — plan accordingly for May 2026 availability.
- **Niche positioning**: target "Angular contractor — enterprise migration & micro-frontends, fintech compliance focus" rather than generic "Senior Angular Developer".
- **Code-as-portfolio strategy**: VatEvidenceSaaS + axiom-dashboard together tell ONE story (one repo with backend reference + frontend, OR two repos cross-linked from each README) — not three disjoint projects.
- **Geographic flexibility**: willing to be in Berlin or Amsterdam 2–3 days/week. NOT willing to relocate full-time.
- **First contract priority**: better to sign a slightly underpriced first contract (€85/h) than wait 3 months for the perfect one. First EU contract reference unblocks rates 20% higher on contract #2.

---

## File map summary

```
C:\Users\david\Documents\Solutions\VatEvidenceSaaS\         ← .NET backend, Clean Architecture
├── VatEvidence.Domain\
├── VatEvidence.Application\
├── VatEvidence.Infrastructure\
├── VatEvidence.Web\
├── docs\
└── CONTRACT_PORTFOLIO_HANDOFF.md  ← this file

C:\Users\david\Documents\webapps\axiom-dashboard\           ← Angular 21 frontend
├── src\app\
│   ├── core\
│   ├── shared\
│   ├── domain\
│   ├── layout\
│   ├── features\
│   └── mocks\
└── package.json
```

---

**End of handoff document. New chat starts below.**
