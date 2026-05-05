# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Personal finance tracker that consolidates CSV exports from multiple banks into a unified categorized ledger. LAN-only, no auth. Multi-family scoped (every entity belongs to a `Family`; the active family is sent on every API request via `X-Family-Id` header).

Stack: .NET 10 Web API · Angular 21 (standalone, SCSS) · PostgreSQL 16 · Ollama (LLM categorization fallback, Phase 3+).

## Development prerequisites

Must be running on the host before starting:
- PostgreSQL 16 on `localhost:5432` with database `transactatrack`
- Ollama on `localhost:11434` with model `llama3.2:1b`
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef --version 10.0.*`

Local credentials go in `src/Transactatrack.Api/appsettings.Development.json` (gitignored). Copy the `.example` file to create it.

## Common commands

```bash
make db-update         # apply pending EF migrations only
make api               # db-update then start the API on :5080
make ui                # ng serve on :4200
make test              # unit + integration tests
make test-unit         # unit tests only
make test-integration  # integration tests only (requires Docker for Testcontainers)
make test-e2e          # Playwright headless (requires API + UI already running)
make test-e2e-ui       # Playwright interactive UI mode
make migrate name=Foo  # scaffold a new EF migration named Foo
```

To run a single integration test class:
```bash
dotnet test tests/Transactatrack.IntegrationTests --filter "FullyQualifiedName~FamilyScopingTests"
```

To reset the dev database:
```bash
dotnet ef database drop -f -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api
make api
```

## Architecture

### .NET project dependencies
```
Domain          (entities, enums, value objects — no dependencies)
  └── Application  (DTOs, IFamilyContext interface — depends on Domain)
        └── Infrastructure  (EF Core, configs, OllamaClient — depends on Application+Domain)
              └── Api  (controllers, middleware, DI wiring — depends on Infrastructure+Application)
```

### Family scoping

Every entity except `Family` itself extends `FamilyScopedEntity` (Id, FamilyId, CreatedUtc). `AppDbContext` registers a global EF query filter on each scoped entity type that automatically restricts all queries to `_familyContext.ActiveFamilyId`.

`FamilyContextMiddleware` (wired before controllers in `Program.cs`) reads the `X-Family-Id` header and populates `FamilyContext` (a scoped service). Missing or malformed header → HTTP 400. Exempt paths: `/api/families`, `/api/status`, and `/health` (checked by prefix).

Standard ASP.NET Core health check endpoints are wired via `AddHealthChecks()`: `/health/live` (liveness — no checks, just 200) and `/health/ready` (readiness — DB + Ollama, tagged "ready"). The richer `/api/status` endpoint (API version, DB, Ollama model list) exists for the Angular dashboard.

`FamilyContext` is registered as `Scoped`, and `IFamilyContext` resolves to the same instance. `AppDbContext.SaveChangesAsync` auto-stamps `FamilyId` and `CreatedUtc` on any `Added` `FamilyScopedEntity` — controllers never set these manually.

### Application layer is intentionally thin

Controllers inject `AppDbContext` directly. `Transactatrack.Application` holds only DTOs and `IFamilyContext`. Service-layer classes are reserved for non-CRUD logic (rule engine, transfer matcher, CSV parsing — Phases 2–4).

### Domain entities

| Entity | Notes |
|---|---|
| `Family` | Root — no `FamilyId`, not query-filtered |
| `Owner` | Person who owns accounts |
| `Account` | Bank account; has `OwnerId`, `AccountType` (enum), optional `BankCode` |
| `Category` | Top-level spend category |
| `SubCategory` | Child of `Category` (non-nullable `CategoryId`); cascade-deleted with parent |
| `Transaction` | Canonical ledger row; `SourceRowHash` enforces dedupe per account |
| `ImportBatch` | Tracks a single CSV upload |
| `CategoryRule` | Rule-engine config (Phase 3) |

Enums (`AccountType`, `ImportBatchStatus`, etc.) are serialized as strings via a global `JsonStringEnumConverter` configured in `Program.cs`.

### Angular architecture

- **`FamiliesService`** owns a `BehaviorSubject<FamilyDto[]>` as the single source of truth for the family list. Any component that mutates families calls `svc.refresh()` — the toolbar and any other subscriber update automatically.
- **`FamilyContextService`** holds the active family ID as a writable signal, persisted to `localStorage`.
- **`familyIdInterceptor`** (functional `HttpInterceptorFn`) injects `X-Family-Id` on every request to `apiBaseUrl`, skipping `GET /api/families` which is unscoped.
- Feature components are lazy-loaded routes. Each uses inline editing or `MatDialog` for mutations — no shared form infrastructure.
- Categories page (`CategoriesPage`) uses `mat-accordion` with inline editing signals instead of dialogs; no `CategoriesTree` or `CategoryEditDialog`.

### Migrations history

| Migration | What it did |
|---|---|
| `InitialEmpty` | Empty schema baseline |
| `Phase1Schema` | All 8 entities (incl. SubCategories), seed default Family |

### Build notes

- Npgsql EF package tracks its own versioning — do not pin it to match EF Core's patch version (e.g. `10.0.7` doesn't exist for Npgsql; use `10.0.*`).
- Angular Material uses M3 theming (`mat.define-theme`). Available palettes are `$azure-palette`, `$violet-palette`, `$rose-palette`, etc. — not the M2 `$indigo-palette`.
- Material Icons are bundled via the `material-icons` npm package (listed in `angular.json` styles), not loaded from Google Fonts CDN.
