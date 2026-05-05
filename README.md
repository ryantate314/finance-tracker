# transactatrack

Personal-finance tracker that consolidates CSV exports from multiple banks into one categorized ledger. LAN-only, family-scoped, no auth.

Stack: ASP.NET Web API (.NET 10) + Angular 21 + PostgreSQL, with Ollama for LLM-based categorization fallback.

## Phase status

- **Phase 0** ✅ — solution scaffold, empty schema, `/api/health`, Angular system-status page.
- **Phase 1** ✅ — 7 domain entities, family scoping via `X-Family-Id` header, CRUD for Families/Owners/Accounts/Categories, Angular Material toolbar with family switcher, 6 integration tests.
- Phase 2+ — see `.claude/plans/i-want-to-build-federated-bengio.md`.

## Prerequisites

Install once on your dev machine:

- .NET 10 SDK
- Node 20+ (Angular 21 supports 20.19+ and 22.12+)
- PostgreSQL 16, running on `localhost:5432`
- Ollama, running on `localhost:11434`
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef --version 10.0.*`

## One-time setup

```bash
# Database
createdb transactatrack
# (or via psql) CREATE DATABASE transactatrack;

# Ollama dev model
ollama pull llama3.2:1b

# Local config (real DB credentials — gitignored)
cp src/Transactatrack.Api/appsettings.Development.json.example \
   src/Transactatrack.Api/appsettings.Development.json
# then edit appsettings.Development.json with your Postgres user/password
```

## Daily quickstart

```bash
# from repo root
dotnet ef database update -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api
dotnet run --project src/Transactatrack.Api
```

In another shell:

```bash
cd src/Transactatrack.Web
npm install   # first time only
ng serve
```

Then browse to http://localhost:4200/status — API, Database, and Ollama rows should all be green.

## Family scoping

Every entity is scoped to a family. The active family is read from the `X-Family-Id` request header on every API call. If the header is absent the API falls back to the seeded default family (`00000000-0000-0000-0000-000000000001`). The Angular app stores the selection in `localStorage` and injects the header automatically via an HTTP interceptor.

To reset the dev DB after schema changes:

```bash
dotnet ef database drop -f -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api
dotnet ef database update -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api
```

## Configuration

`appsettings.json` ships with placeholder defaults. Real values go in `appsettings.Development.json` (gitignored) or in environment variables:

```bash
ConnectionStrings__Default="Host=localhost;Port=5432;Database=transactatrack;Username=...;Password=..."
Ollama__BaseUrl="http://localhost:11434"
```

## Repository layout

```
transactatrack/
├── src/
│   ├── Transactatrack.Domain/            # entities, value objects (Phase 1+)
│   ├── Transactatrack.Application/       # use cases, DTOs
│   ├── Transactatrack.Infrastructure/    # EF Core, parsers, OllamaClient
│   ├── Transactatrack.Api/               # ASP.NET Web API
│   └── Transactatrack.Web/               # Angular workspace
├── tests/
│   ├── Transactatrack.UnitTests/
│   └── Transactatrack.IntegrationTests/
└── .claude/plans/                        # phased build plans
```

`deploy/` will land in Phase 6 (production Portainer deploy). Local dev is host-native — no compose file.
