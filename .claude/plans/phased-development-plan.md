# Transactatrack — Phased Build Plan to First Usable Release

## Context

Greenfield personal finance tracker. The user (Ryan) wants to consolidate CSV exports from multiple banks into one unified, categorized ledger so he can see where the household's money is going. The system must understand multiple Owners (self, wife, joint) and avoid double-counting transfers between accounts (e.g. a credit card payment showing as both a debit and credit).

Stack: ASP.NET Web API (controllers) + Angular SPA + PostgreSQL, deployed as Docker containers on a Portainer VM in the home lab.

This file captures phased build steps only — not full implementation detail. A later agent will pick up each phase, plan it in depth, and execute.

## Locked-in Decisions

- **Auth**: None (LAN-only). System still supports multiple **Families**; user toggles active family in the UI. Every domain entity is family-scoped.
- **Categorization**: Rule-based engine first; **Ollama** (self-hosted, separate container) as LLM fallback when no rule matches.
- **Transfers**: Auto-matched with no user confirmation. Manual unlink available as escape hatch.
- **v1 scope**: CSV import + unified ledger + rule categorization + transfer handling + spending dashboard.
- **Banks**: One concrete bank parser in v1; parser abstraction designed so additional banks slot in later.
- **Deployment**: Plain HTTP containers behind user's existing reverse proxy (proxy choice confirmed at deploy time).
- **Stack versions**: .NET 10, Angular CLI 21 (standalone, SCSS), Postgres 16, Serilog (console sink), Ollama with `llama3.2:1b` for dev. Production Ollama model still TBD.
- **Dev vs deploy split**: Local dev runs natively on the host (assumes Postgres + Ollama already installed). Containers are used **only** for production deployment in Phase 6. The `deploy/` folder is intentionally absent until then.
- **UI library**: Angular Material (M3, azure/rose theme).
- **Application layer pattern**: thin — controllers query `AppDbContext` directly. Service layer reserved for non-CRUD logic (parsing, rule engine, transfer matching).
- **Active family contract**: `X-Family-Id` request header; absent → seed default (`SeedIds.DefaultFamilyId = 00000000-0000-0000-0000-000000000001`); malformed → 400.

## Repository Layout (target)

```
transactatrack/
├── src/
│   ├── Transactatrack.Domain/            # entities, value objects, domain services
│   ├── Transactatrack.Application/       # use cases, DTOs, interfaces
│   ├── Transactatrack.Infrastructure/    # EF Core, Postgres, parsers, Ollama client
│   ├── Transactatrack.Api/               # ASP.NET Web API controllers, DI, startup
│   └── Transactatrack.Web/               # Angular workspace
├── tests/
│   ├── Transactatrack.UnitTests/
│   └── Transactatrack.IntegrationTests/  # Testcontainers-Postgres
├── deploy/
│   ├── docker-compose.dev.yml
│   ├── docker-compose.prod.yml
│   └── Dockerfile.api / Dockerfile.web
└── README.md
```

---

## Phase 0 — Scaffold & Local Dev Loop ✅ Complete

**Completed (2026-05-05):** Solution (`Transactatrack.slnx`) + 4 src projects (`Domain`, `Application`, `Infrastructure`, `Api`) + 2 test projects on .NET 10. EF Core 10 + Npgsql wired; empty `AppDbContext` with `InitialEmpty` migration applied to host Postgres. `GET /api/health` controller checks API/DB/Ollama and returns per-component status. Angular 21 workspace (standalone, SCSS) with routing; `/status` page renders all three components green against host-native Postgres and Ollama (`llama3.2:1b`). Serilog console logging wired; CORS allows `http://localhost:4200` in Development. API pinned to port 5080 via `launchSettings.json`. `appsettings.Development.json` is gitignored; `.example` checked in. README documents the dev quickstart. Native dev only — `deploy/` is empty by design.

**Goal**: API + Angular run natively on the host, talking to host-native Postgres and Ollama; `/api/health` confirms all three components.

- Create solution + projects per layout above.
- Angular workspace with routing + a placeholder shell.
- EF Core + Npgsql wired; empty `AppDbContext`; first (empty) migration.
- Host-native Postgres + Ollama (already installed on Ryan's dev machine; no compose file).
- `/health` controller; Angular calls it from a "system status" page to prove end-to-end wiring.
- Decide on logging (Serilog → console) and config (`appsettings.json` + `appsettings.Development.json` + env vars).

**Verify**: `dotnet run --project src/Transactatrack.Api` + `ng serve` from `src/Transactatrack.Web` → `http://localhost:4200/status` shows API + DB + Ollama all green and lists Ollama model availability.

---

## Phase 1 — Core Domain + Family Scoping ✅ Complete

**Completed (2026-05-05):** All 7 domain entities (`Family`, `Owner`, `Account`, `Category`, `Transaction`, `ImportBatch`, `CategoryRule`) added with `FamilyScopedEntity` base class and `Phase1Schema` migration applied. EF Core global query filters scope every entity by `X-Family-Id` header (absent → seed default `00000000-0000-0000-0000-000000000001`). REST controllers for Families, Owners, Accounts, Categories with `DeleteBehavior.Restrict` + 409 on FK violation. Angular Material (azure/rose M3 theme) wired; `FamilyContextService` (signal + localStorage) + `familyIdInterceptor` in place; toolbar family switcher and 4 feature pages (list + edit-dialog each). 6/6 integration tests green (Testcontainers.PostgreSql). Application layer is thin — controllers query `AppDbContext` directly.

**Goal**: CRUD the building blocks, with every record scoped to a Family.

- Entities: `Family`, `Owner`, `Account` (with `OwnerId`, `AccountType`, `Institution`), `Category` (hierarchical, parent/child), `Transaction` (canonical model — date, posted date, amount, description, merchant, accountId, categoryId, isTransfer, transferGroupId, importBatchId, sourceRowHash), `ImportBatch`, `CategoryRule`.
- `FamilyId` on every non-root entity. EF Core global query filter applies the active family automatically.
- Active family resolved from request header `X-Family-Id` (with a default-family seed for first run).
- REST controllers + Angular pages for managing Families, Owners, Accounts, Categories.
- Family switcher in Angular toolbar; persists selection in `localStorage`.

**Verify**: Create two families, confirm data isolation by switching; full CRUD round-trips work in the UI.

---

## Phase 2 — CSV Import (one bank) ✅ Complete

**Completed (2026-05-06):** `IBankCsvParser` + `IBankParserRegistry` abstractions in Application; `ChaseParser` (CsvHelper-based, BankCode `"Chase"`) in Infrastructure. `SourceRowHasher` produces SHA-256 hex of `accountId|yyyy-MM-dd|amount(invariant)|normalized(description)` and feeds the existing `(AccountId, SourceRowHash)` unique index for dedup. `ImportService` (Infrastructure) handles upload → persist as `Pending` → flip on commit / delete on discard, with parser dispatch via `Account.BankCode` (null → 400, unknown → 400, existing pending → 409). `ImportsController` (`POST /api/imports` multipart, GET list + by id, `POST /commit` and `/discard`) and `TransactionsController` (`GET /api/transactions` paged with `accountIds`, `categoryIds`, `from`, `to`, `q`; `EF.Functions.ILike` for case-insensitive search; ledger filtered to `Status == Committed`). Angular: lazy-loaded `/imports`, `/imports/:id`, `/ledger`. Imports page lists batches; upload dialog filters accounts to those with `BankCode` set; preview page shows metadata + sample rows + Commit / Discard buttons; ledger page uses `mat-paginator`, native datepickers, multi-select chips for accounts/categories, debounced search. Tests: 13 unit (Chase parser, SourceRowHasher), 20 integration (upload/commit/discard, dedup intra-file, scoping, paging/filters) — all green. No new EF migration required (Phase 1 schema was already sufficient).

**Goal**: Upload a CSV from the chosen bank → see rows in the unified ledger.

- `IBankCsvParser` abstraction returning a stream of canonical `ParsedTransaction` rows; register parsers by `BankCode`.
- One concrete parser for the chosen bank (user provides sample CSVs to fix the format).
- `POST /api/imports`: multipart upload, `accountId` + `bankCode`, returns an `ImportBatch` with parsed rows in `Pending` state for preview.
- `POST /api/imports/{id}/commit`: persists rows; deduplicates against existing `Transaction` via natural-key hash (account + date + amount + normalized description).
- Angular: import wizard — pick account → upload → preview table with dedupe annotations → commit.
- Unified ledger page: server-side paged, sortable, filterable list of transactions across all accounts in the active family.

**Verify**: Re-importing the same CSV produces zero new transactions. Importing two different statements yields one merged ledger.

---

## Phase 3 — Rule-Based Categorization + Ollama Fallback ✅ Complete

**Completed (2026-05-06):** `CategorizationSource` enum (`Manual`/`Rule`/`Llm`), `LlmCategorizationStatus` enum, and `Phase3CategorizationFields` migration applied — adds `CategorizationSource`, `NeedsReview`, `LlmConfidence`, `LlmModel`, `AppliedRuleId` (FK → CategoryRules, ON DELETE SET NULL), `CategorizedUtc` to Transactions; `LlmStatus`/`LlmRowsTotal`/`LlmRowsDone` to ImportBatches; `AmountMin`/`AmountMax` to CategoryRules. `RuleEngine` in `Infrastructure/Categorization/` (priority order, case-insensitive contains/equals, 100ms regex timeout, AmountRange via `Math.Abs`). `CategorizationService` runs rules synchronously at upload time and starts background LLM work via `IServiceScopeFactory` + fire-and-forget `Task.Run`; persists progress on `ImportBatch` for polling. `OllamaCategorizer` wraps `OllamaClient.GenerateJsonAsync` (new `POST /api/generate` method) with `SemaphoreSlim(1,1)`, 5-row batches, integer-ID prompts, and validates JSON response. `CategoryRulesController` with CRUD + bulk reorder + regex/AmountRange validation (400). `TransactionsController` gains `PATCH /api/transactions/{id}` (no status filter — works for Pending + Committed) and `needsReview` filter. `ImportsController` drops 50-row limit on detail, adds `POST /rerun-rules` and `POST /suggest-llm` (202 Accepted). Angular: `category-rules.service.ts`, `rules-page.ts` (CDK drag-drop), `rule-edit-dialog.ts`, `transactions.service.ts` (PATCH). Import preview page gains Category `mat-select` per row (inline PATCH), AI/Rule source chips, "Re-run Rules" + "Suggest with AI" buttons, `mat-progress-bar` polling. Ledger page gains Category `mat-select` per row, "Needs review" `mat-checkbox` filter. `/rules` route + "Rules" nav link. Tests: 16 unit (35 total), 12 new integration (26 total) including `RulesTests`, `RuleApplicationTests`, `LlmTests` with stub `IOllamaCategorizer`.

**Goal**: New imports come in pre-categorized; un-matched rows get an LLM suggestion.

- `CategoryRule`: priority, match-field (description/merchant/amount-range), match-type (contains/regex/equals), target `CategoryId`, scope (family-wide vs per-account).
- Rule engine: deterministic, evaluated in priority order, first match wins.
- Pipeline runs at import-commit time; can be re-run on demand for a date range.
- Ollama client: HTTP call to local container; prompt includes the family's category list and the transaction's merchant/description/amount; returns a category + confidence.
- Persist LLM result with `Source` (rule / llm / manual) and `NeedsReview` flag when confidence is low.
- **Categorization runs at upload time** (against the Pending batch), so rows arrive in the import-preview page already categorized. The user adjusts mappings on the preview before clicking Commit — Phase 2 deferred this UI here on purpose so the rule engine could populate the same column. Adds a category column with inline `mat-select` editing on `import-preview-page.ts`, plus a backend endpoint to update `Transaction.CategoryId` (works for Pending and Committed rows so the same control serves both pages).
- Angular: rule editor (CRUD + drag-to-reorder priority), per-transaction recategorize control on both import-preview and ledger, "needs review" filter on the ledger (good for spot-fixing rule mistakes after commit).

**Verify**: Add a rule "Description contains COSTCO → Groceries"; re-run categorization; matching rows reclassify. Disable the rule; un-matched row gets an Ollama suggestion within a few seconds.

---

## Phase 4 — Transfer Auto-Match

**Goal**: Credit-card payments and inter-account moves don't inflate spending totals.

**Foundation already in place** (delivered alongside the analytics page):
- `CategoryKind` enum on `Category` (`User`, `Transfer`); a per-family system `Transfer` category is seeded on family creation and cannot be deleted or renamed.
- `Transaction.IsTransfer` is auto-synced from the chosen category's `Kind` in the manual `PATCH /api/transactions/{id}` path and in the rule-engine application path (both initial import and rerun).
- LLM categorizer hides system categories from the prompt, so the model can never suggest "Transfer".
- All spending aggregations already exclude `IsTransfer = true` (`AnalyticsController.cs`).

**Still TODO for Phase 4**:
- `TransferMatcher` service: pairs transactions where amounts are equal-and-opposite within ±N days (configurable, default 3) across two accounts in the same family. Sets `IsTransfer = true`, joins via `TransferGroupId`, and assigns the family's system Transfer category so the ledger displays consistently with manual tagging.
- Runs on every import-commit and as a manual "rescan transfers" action.
- Angular: visual transfer badge in the ledger; right-click → "Unlink transfer" escape hatch (clears `TransferGroupId` and reverts category/IsTransfer on both sides).

**Verify**: Import a credit-card statement and a checking statement that pays it; the matching pair shows a transfer badge and disappears from category totals.

---

## Phase 5 — Spending Dashboard

**Goal**: Answer "where did our money go this month?" in one screen.

- Aggregation endpoints (server-side, indexed):
  - Spend by category (with drill-down to subcategories)
  - Spend by owner
  - Spend over time (month / week buckets)
  - Account balances (running)
- Common filters: date range, owner(s), account(s), category(ies), exclude/include transfers.
- Angular dashboard: pick chart library (ng2-charts or ngx-charts) at execution time. Layout: KPI tiles → category donut → time-series line → transactions drilldown table.

**Verify**: Filter to "last 30 days, joint accounts only, exclude transfers" and totals match a hand-computed sum from the ledger.

**Aggregation hook**: prefer `Category.Kind` (User / Transfer / Income — see `.claude/plans/income-system-category.md`) over names, signs, or per-report rules. New report types should bucket on `Kind`; if a third semantic bucket is needed, extend the enum and seed a system category rather than re-inventing classification logic per endpoint.

---

## Phase 6 — Production Deploy to Portainer

**Goal**: Stack runs on the home-lab Portainer VM behind the user's existing reverse proxy.

- Multi-stage `Dockerfile.api` (publish → runtime image).
- Multi-stage `Dockerfile.web` (Angular build → nginx static).
- `docker-compose.prod.yml`: `api`, `web`, `postgres` (named volume), `ollama` (named volume for models). No proxy in this stack — exposed ports get attached to the existing reverse proxy network.
- Postgres backup: nightly `pg_dump` to a host-mounted volume; document restore.
- Document required env vars (DB connection, Ollama URL, default family seed).
- Smoke checklist on Portainer: deploy stack → import a real CSV → confirm dashboard renders.

**Verify**: After a fresh deploy, the import → categorize → dashboard flow works against real bank data, and a `pg_dump` snapshot can be restored into a clean container.

---

## Critical Files (created during execution)

- `src/Transactatrack.Domain/Entities/*.cs` — Family, Owner, Account, Transaction, Category, CategoryRule, ImportBatch
- `src/Transactatrack.Infrastructure/Persistence/AppDbContext.cs` — EF Core + family query filter
- `src/Transactatrack.Application/Imports/IBankCsvParser.cs` + `src/Transactatrack.Infrastructure/Imports/Parsers/ChaseParser.cs`
- `src/Transactatrack.Infrastructure/Llm/OllamaClient.cs` (GetTagsAsync + GenerateJsonAsync)
- `src/Transactatrack.Infrastructure/Categorization/RuleEngine.cs` + `CategorizationService.cs` + `OllamaCategorizer.cs`
- `src/Transactatrack.Application/Transfers/TransferMatcher.cs` (Phase 4 — not yet created)
- `src/Transactatrack.Api/Controllers/*.cs` — Imports, Transactions, Categories, Rules, Reports, Families/Owners/Accounts
- `src/Transactatrack.Web/src/app/**` — family switcher, import wizard, ledger, rules, dashboard
- `deploy/docker-compose.dev.yml`, `deploy/docker-compose.prod.yml`, `deploy/Dockerfile.api`, `deploy/Dockerfile.web`

## Open Items (decide at execution time)

- Reverse-proxy product in use (Traefik / Nginx Proxy Manager / Caddy / other) — affects Phase 6 wiring only.
- **Production** Ollama model choice (e.g. `llama3.1:8b` vs smaller) — dev uses `llama3.2:1b`; pick prod model based on the VM's available RAM.
- Angular chart library (ng2-charts vs ngx-charts).
- Additional bank parsers — Chase is the v1 target (locked in 2026-05-06); add more `IBankCsvParser` implementations as new banks come up.
