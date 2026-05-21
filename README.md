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

## Home-server deploy

The production target is a single home server running Docker + Portainer. The
stack is two images — `transactatrack-api` (.NET API) and `transactatrack-web`
(nginx serving the Angular SPA and reverse-proxying `/api` to the API). Postgres
stays on the host (reached via `host.docker.internal`); Ollama already runs on a
separate workstation.

### One-time home-server setup

1. **Stand up the self-hosted GHA runner** as a Portainer stack from
   `deploy/runner-stack.yml` — see [GHA runner stack](#gha-runner-stack) below.
   Confirm it appears in GitHub → Settings → Actions → Runners as **Idle**.
2. **Create the app Portainer stack** from `deploy/docker-compose.yml`. Set the
   env vars from `deploy/.env.example` (`POSTGRES_CONNECTION`, `OLLAMA_BASE_URL`,
   `WEB_HOST_PORT`).
3. **Enable the stack webhook** in Portainer (Stack → Webhooks → Create) and add
   the URL as a GHA repo secret named `PORTAINER_WEBHOOK`.
4. **Create the Postgres database** on the host:
   `sudo -u postgres createdb transactatrack`. Migrations run on API startup
   because `Database__AutoMigrate=true` is set in the compose file.

### Deploy loop

Push to `main` → `.github/workflows/deploy.yml` runs on the home-server runner
→ both images are built locally (no registry round-trip) → the workflow hits the
Portainer webhook → the stack recreates containers using the freshly-tagged
`:latest` images. Expected end-to-end: ~1–2 minutes.

`workflow_dispatch` is enabled if you want to redeploy without pushing.

### Local compose run (optional)

```bash
cd deploy && cp .env.example .env  # edit DB password
docker compose build
docker compose up
```

### GHA runner stack

`deploy/runner-stack.yml` runs the GitHub Actions runner itself as a Portainer
stack — independent of the app stack so app restarts don't take the runner
offline. Uses the community image `myoung34/github-runner` with the host's
Docker socket mounted in so the runner can `docker build` against the host
daemon (no Docker-in-Docker).

Setup:

1. **Generate a GitHub PAT** with the `repo` scope (classic) or a fine-grained
   token scoped to this repo with `Actions: Read and write`. Save the value.
2. **Create a new Portainer stack** from `deploy/runner-stack.yml`. Set the env
   vars from `deploy/runner.env.example`: `REPO_URL`, `ACCESS_TOKEN`,
   `RUNNER_NAME`.
3. **Verify** in GitHub → Settings → Actions → Runners that the runner appears
   with labels `self-hosted,home-server` and status `Idle`. The deploy workflow
   targets `runs-on: [self-hosted, home-server]` so the labels must match. The
   `home-server` label is intentionally generic — point other personal repos at
   this same runner by giving their workflows the same `runs-on`.

Security caveat: mounting `/var/run/docker.sock` gives this container root on
the Docker daemon. Acceptable for a LAN-only personal box but not a pattern to
copy onto anything internet-facing.
