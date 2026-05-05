# Phase 1 — Core Domain + Family Scoping (Detailed)

## Context

Phase 0 stood up the empty skeleton — solution, DbContext with no entities, `/api/health`, Angular `/status` page. Phase 1 fills the skeleton with the canonical domain model and the family-scoping mechanism that every later phase relies on. After Phase 1, Ryan can create two families in the UI, switch between them, and CRUD owners/accounts/categories with full data isolation. Transaction/ImportBatch/CategoryRule tables also land now (so Phase 2/3 only ALTER columns), but their UI/controllers come later.

## Decisions confirmed this session

- **UI library**: Angular Material (added to Web workspace; theme: Indigo/Pink default for Phase 1, can re-theme later).
- **Application layer**: thin — controllers inject `AppDbContext` directly. The `Transactatrack.Application` project gets DTOs only; it stays free of services until real logic emerges in Phase 2/3.
- **Schema scope**: all 7 entities in a single `Phase1Schema` migration. Phase 2/3 only add columns to `Transaction` (`Source`, `NeedsReview`).
- **ID type**: `Guid` everywhere, server-generated (`ValueGeneratedOnAdd`, no client supplies IDs on create).
- **Default seed family**: fixed Guid `00000000-0000-0000-0000-000000000001`, name `"Default"`, inserted via `HasData` in the migration.
- **Active family resolution**: middleware reads `X-Family-Id` header; missing → default seed; malformed Guid → HTTP 400.
- **Family entity is NOT family-scoped** — it is the root. All other entities have `FamilyId` and the global query filter.
- **Hard deletes only**. FKs from scoped entities use `DeleteBehavior.Restrict`. Family delete is rejected at DB level if any scoped row references it; controller translates the `DbUpdateException` to HTTP 409.
- **Validation**: built-in `DataAnnotations` on DTOs (`[Required]`, `[StringLength]`). No FluentValidation in Phase 1.
- **Enum storage**: stored as `int` in Postgres (EF default). Friendlier-for-DBA `string` storage can land later if needed.

## Tasks

### 1. Domain entities

Add under `src/Transactatrack.Domain/`:

```
Common/
  FamilyScopedEntity.cs          // abstract: Id (Guid), FamilyId (Guid), CreatedUtc (DateTime)
  SeedIds.cs                     // public static class: DefaultFamilyId = Guid.Parse("...01")
Enums/
  AccountType.cs                 // Checking, Savings, CreditCard, Loan, Investment, Cash, Other
  ImportBatchStatus.cs           // Pending, Committed, Discarded
  RuleMatchField.cs              // Description, Merchant, AmountRange
  RuleMatchType.cs               // Contains, Equals, Regex
  RuleScope.cs                   // Family, Account
Entities/
  Family.cs                      // Id, Name, CreatedUtc        (root — no FamilyId)
  Owner.cs                       // : FamilyScopedEntity + Name
  Account.cs                     // : FamilyScopedEntity + OwnerId, Name, Institution, AccountType, BankCode (string?)
  Category.cs                    // : FamilyScopedEntity + ParentId (Guid?), Name
  Transaction.cs                 // : FamilyScopedEntity + AccountId, Date, PostedDate (DateTime?),
                                 //    Amount (decimal), Description, Merchant (string?),
                                 //    CategoryId (Guid?), IsTransfer (bool), TransferGroupId (Guid?),
                                 //    ImportBatchId, SourceRowHash (string)
  ImportBatch.cs                 // : FamilyScopedEntity + AccountId, BankCode, OriginalFilename,
                                 //    UploadedUtc, Status (ImportBatchStatus)
  CategoryRule.cs                // : FamilyScopedEntity + Priority, MatchField, MatchType,
                                 //    Pattern, TargetCategoryId, Scope, AccountId (Guid?), IsEnabled
```

POCO style — no behavior, no domain services in Phase 1. `FamilyScopedEntity` is just a base for shared columns; it does not enforce scoping (that is the DbContext's job).

### 2. EF configuration & DbContext

- `IFamilyContext` lives in **Application** (`src/Transactatrack.Application/IFamilyContext.cs`): `interface IFamilyContext { Guid ActiveFamilyId { get; } }`.
- `FamilyContext` (Infrastructure, internal): mutable concrete `class FamilyContext : IFamilyContext { public Guid ActiveFamilyId { get; set; } }`. Registered as `Scoped`. Both `IFamilyContext` and the concrete `FamilyContext` resolve to the same instance per request.
- `src/Transactatrack.Infrastructure/Persistence/Configurations/` with one `IEntityTypeConfiguration<T>` per entity. Sets:
  - PK on `Id`; `ValueGeneratedOnAdd()`.
  - String length limits (`Name` 200, `Description` 500, etc.).
  - FKs with `DeleteBehavior.Restrict` (Family→Owner, Owner→Account, Account→Transaction, Account→ImportBatch, ImportBatch→Transaction, Category→Category self-ref, Category→Transaction, Category→CategoryRule).
  - Indexes on `(FamilyId)` for every scoped table; composite `(FamilyId, AccountId, Date)` on `Transaction`; unique `(AccountId, SourceRowHash)` on `Transaction` (sets up Phase 2 dedupe).
  - `HasData` for the seed default `Family` in the `Family` configuration.
- `AppDbContext.cs` updates:
  - Constructor takes `(DbContextOptions<AppDbContext>, IFamilyContext)`.
  - `DbSet<>` for all 7 entities.
  - `OnModelCreating`:
    - `ApplyConfigurationsFromAssembly`.
    - For each `FamilyScopedEntity` descendant, register a global query filter: `entity => EF.Property<Guid>(entity, "FamilyId") == _familyContext.ActiveFamilyId`.
  - Override `SaveChangesAsync` to stamp `CreatedUtc = DateTime.UtcNow` on `Added` entries and auto-set `FamilyId = _familyContext.ActiveFamilyId` on `Added` `FamilyScopedEntity` entries (so controllers don't have to remember).

### 3. Family-context middleware

`src/Transactatrack.Api/Middleware/FamilyContextMiddleware.cs`:

- On every request:
  - Header `X-Family-Id` present and parses as Guid → set `FamilyContext.ActiveFamilyId`.
  - Present but malformed → short-circuit with `400 Bad Request` (`ProblemDetails`, `title = "Invalid X-Family-Id"`).
  - Absent → set `ActiveFamilyId = SeedIds.DefaultFamilyId`.
- Wired in `Program.cs` after `UseRouting` but before endpoints (so the value is available inside controller actions / DbContext).
- The middleware does **not** validate that the family exists in the DB — the query filter naturally returns empty results if the ID is bogus, and validating costs a query per request.

### 4. Application DTOs

```
src/Transactatrack.Application/
  Families/    FamilyDto.cs, CreateFamilyRequest.cs, UpdateFamilyRequest.cs
  Owners/      OwnerDto.cs, CreateOwnerRequest.cs, UpdateOwnerRequest.cs
  Accounts/    AccountDto.cs, CreateAccountRequest.cs, UpdateAccountRequest.cs
  Categories/  CategoryDto.cs, CreateCategoryRequest.cs, UpdateCategoryRequest.cs
```

Plain records with `[Required]` / `[StringLength]` annotations. No mappers — controllers project manually with `Select(...)` (one-line; no AutoMapper).

### 5. API controllers

Under `src/Transactatrack.Api/Controllers/`:

- `FamiliesController` — `[Route("api/families")]`. Family has no query filter, so List/Get just query the DbSet directly. `Delete` catches `DbUpdateException` from FK violations and returns `409 Conflict` with `"Family has dependent records"`.
- `OwnersController` — `[Route("api/owners")]`. Standard CRUD; relies on global filter so all queries are auto-scoped.
- `AccountsController` — `[Route("api/accounts")]`. Validates `OwnerId` exists in active family; returns `400` if not.
- `CategoriesController` — `[Route("api/categories")]`. Validates `ParentId` (if provided) exists in active family. List endpoint returns flat list with `ParentId` (UI builds the tree).

All actions:
- Async; return `ActionResult<T>` or `ActionResult<IEnumerable<T>>`.
- `POST` returns `201 Created` with `Location` header.
- `PUT /{id}` returns `204 No Content`; `404` if not found.
- `DELETE /{id}` returns `204`; `404` if not found; `409` on FK violation.
- Use `await db.<Set>.FindAsync(id)` for Get-by-Id (respects query filter); `FindAsync` returns null when the ID belongs to a different family — so cross-family access naturally surfaces as 404.

### 6. Migration

```bash
dotnet ef migrations add Phase1Schema \
  -p src/Transactatrack.Infrastructure \
  -s src/Transactatrack.Api \
  -o Persistence/Migrations
dotnet ef database update -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api
```

Verify after applying:
- `psql -d transactatrack -c '\dt'` shows 7 new tables + `__EFMigrationsHistory`.
- `psql -d transactatrack -c 'SELECT id, name FROM "Families";'` returns the seeded default row.
- `psql -d transactatrack -c '\d "Transactions"'` shows the composite index `(FamilyId, AccountId, Date)` and unique `(AccountId, SourceRowHash)`.

### 7. Angular: shared infrastructure

Under `src/Transactatrack.Web/src/app/`:

- `core/family-context/family-context.service.ts`:
  - Writable signal `activeFamilyId = signal<string | null>(null)`.
  - Loads from `localStorage.getItem('transactatrack.activeFamilyId')` on construction.
  - `setActive(id)` → updates signal + writes to localStorage.
- `core/family-context/family-id.interceptor.ts`:
  - Functional `HttpInterceptorFn`.
  - Reads `activeFamilyId` from the service.
  - Adds header `X-Family-Id: <id>` to any request whose URL starts with `environment.apiBaseUrl`.
  - **Skips** the header for `GET /api/families` (that endpoint is not family-scoped, and we don't want a stale ID confusing things during initial bootstrap).
- `app.config.ts` — register the interceptor via `provideHttpClient(withInterceptors([familyIdInterceptor]))`. Also add `provideAnimations()` for Material.
- `core/api/api-error.ts` — small helper to extract `ProblemDetails.title`/`detail` for snackbar display.
- Angular Material setup:
  - `npm i @angular/material @angular/cdk`
  - `ng add @angular/material` (Indigo/Pink theme, typography, animations).
  - Material components imported per standalone component (no shared module).

### 8. Angular: feature pages

```
features/families/      list, edit-dialog, families.service.ts
features/owners/        list, edit-dialog, owners.service.ts
features/accounts/      list, edit-dialog, accounts.service.ts
features/categories/    tree, edit-dialog, categories.service.ts
```

UX per feature:
- **List**: `MatTable` (columns: Name + entity-specific fields + Edit/Delete). `MatButton` "New".
- **Edit dialog**: `MatDialog` with `ReactiveFormsModule` + `MatFormField`/`MatInput`/`MatSelect`. Save calls service → on success closes dialog and refreshes list. On 4xx, shows the `ProblemDetails.title` in a `MatSnackBar`.
- **Categories**: list endpoint returns flat array; the component builds a tree (`MatTree` with `NestedTreeControl`). Edit dialog has a parent dropdown listing all categories except the one being edited and its descendants (cycle prevention).
- **Accounts**: edit dialog has Owner dropdown (driven by `OwnersService.list()`) and AccountType dropdown (driven by an enum constant on the client).

Routes added to `app.routes.ts`:

```ts
{ path: 'families', loadComponent: () => import('./features/families/families-list').then(m => m.FamiliesList) },
{ path: 'owners', loadComponent: () => import('./features/owners/owners-list').then(m => m.OwnersList) },
{ path: 'accounts', loadComponent: () => import('./features/accounts/accounts-list').then(m => m.AccountsList) },
{ path: 'categories', loadComponent: () => import('./features/categories/categories-tree').then(m => m.CategoriesTree) },
```

### 9. Angular: toolbar family switcher

Update `app.html` / `app.ts`:

- Toolbar becomes a `MatToolbar` with:
  - `<h1>transactatrack</h1>` on the left.
  - Nav links (`MatButton routerLink`): Status, Families, Owners, Accounts, Categories.
  - Right-aligned `MatSelect` bound to `FamilyContextService.activeFamilyId`.
- On bootstrap, `app.ts` calls `FamiliesService.list()` (which skips the X-Family-Id header) to populate the dropdown.
- If localStorage is empty, default-select the seed `Default` family. If localStorage points to a now-deleted family, fall back to the first available and update localStorage.
- Switching the dropdown updates the service signal; downstream components using `effect()` or the signal directly will refetch.

### 10. Tests

`tests/Transactatrack.IntegrationTests/`:

- Add NuGet: `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing`.
- `IntegrationTestFactory : WebApplicationFactory<Program>` — boots a Postgres container per test class, applies migrations, hands a configured `HttpClient`.
- Helper to set the `X-Family-Id` header on the test `HttpClient`.
- Test cases:
  - `Families_PostThenGet_RoundTrips`
  - `Owners_AreScopedToActiveFamily` (create owner in family A; switch header to family B; list returns empty)
  - `Cannot_GetEntity_FromOtherFamily_Returns404` (using Owner)
  - `DeleteFamily_WithDependentOwner_Returns409`
  - `Categories_CanCreateChildAndList_FlatWithParentId`
  - `Accounts_RejectInvalidOwnerId_Returns400`

`tests/Transactatrack.UnitTests/`:

- Skip for Phase 1. No domain logic worth unit-testing yet (entities are POCOs). The integration suite covers the family-scoping behavior, which is the only non-trivial mechanism added.

### 11. README updates

Add a section documenting:
- The `X-Family-Id` header convention and the default seed family.
- How to reset the dev DB (`dotnet ef database drop -f && dotnet ef database update`).
- Phase 1 status check.

## Critical files

- `src/Transactatrack.Domain/Common/FamilyScopedEntity.cs`, `Common/SeedIds.cs`
- `src/Transactatrack.Domain/Entities/{Family,Owner,Account,Category,Transaction,ImportBatch,CategoryRule}.cs`
- `src/Transactatrack.Domain/Enums/{AccountType,ImportBatchStatus,RuleMatchField,RuleMatchType,RuleScope}.cs`
- `src/Transactatrack.Application/IFamilyContext.cs`
- `src/Transactatrack.Application/{Families,Owners,Accounts,Categories}/*.cs` (DTOs)
- `src/Transactatrack.Infrastructure/Persistence/AppDbContext.cs` (rewritten)
- `src/Transactatrack.Infrastructure/Persistence/FamilyContext.cs`
- `src/Transactatrack.Infrastructure/Persistence/Configurations/*.cs` (7 files)
- `src/Transactatrack.Infrastructure/Persistence/Migrations/<ts>_Phase1Schema.cs`
- `src/Transactatrack.Api/Middleware/FamilyContextMiddleware.cs`
- `src/Transactatrack.Api/Controllers/{Families,Owners,Accounts,Categories}Controller.cs`
- `src/Transactatrack.Api/Program.cs` (DI registrations + middleware wiring)
- `src/Transactatrack.Web/src/app/core/family-context/{family-context.service.ts,family-id.interceptor.ts}`
- `src/Transactatrack.Web/src/app/app.config.ts` (interceptor + animations)
- `src/Transactatrack.Web/src/app/app.routes.ts` (new feature routes)
- `src/Transactatrack.Web/src/app/app.{ts,html,scss}` (toolbar with family switcher)
- `src/Transactatrack.Web/src/app/features/{families,owners,accounts,categories}/**`
- `tests/Transactatrack.IntegrationTests/**` (factory + 6 test cases)
- `README.md`

## Verification (end-to-end)

Preconditions: Phase 0 working state (Postgres up, Ollama up, `/status` green).

1. `dotnet ef database update -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api` succeeds; `psql -d transactatrack -c '\dt'` shows 7 new tables; `SELECT * FROM "Families";` shows the seeded `Default` row.
2. `dotnet test` runs both projects; integration tests green (Testcontainers spins up Postgres per class).
3. `dotnet run --project src/Transactatrack.Api` + `cd src/Transactatrack.Web && ng serve`.
4. Open `http://localhost:4200/families` → see `Default`. Create `Smith Household` and `Test Household`.
5. Toolbar dropdown lists three families. Select `Smith Household`.
6. Navigate to Owners; create `Ryan` and `Spouse`. Navigate to Accounts; create `Chase Checking` (owner Ryan, type Checking, institution Chase). Navigate to Categories; create `Food` and a child `Groceries` under `Food` (tree view shows the nesting).
7. Switch toolbar to `Test Household`. Owners/Accounts/Categories pages all show empty lists. Switch back; data reappears.
8. Curl manual cross-family check: `curl -H "X-Family-Id: <test-household-id>" http://localhost:5080/api/owners/<smith-owner-id>` returns `404`.
9. Try to delete `Smith Household` while it has owners — UI shows snackbar "Family has dependent records" (409 from API). Delete the children first, then the family — succeeds.
10. Refresh the browser → toolbar selection persists (localStorage).
11. Re-run `/status` page → still green (no regressions).

## Deferred to later phases

- Transaction CRUD/UI — Phase 2 (CSV import flow).
- ImportBatch upload + commit endpoints — Phase 2.
- CategoryRule editor + rule-engine evaluation — Phase 3.
- `Source` / `NeedsReview` columns on `Transaction` — Phase 3 migration.
- Transfer matching (`IsTransfer` / `TransferGroupId` are persisted now but not populated) — Phase 4.
- Soft delete, audit columns beyond `CreatedUtc`, optimistic concurrency tokens — not in scope.

## Post-completion housekeeping

After all verification steps pass, edit the parent plan at `.claude/plans/i-want-to-build-federated-bengio.md`:

### A. Mark Phase 1 complete

Replace the `## Phase 1 — Core Domain + Family Scoping` heading with `## Phase 1 — Core Domain + Family Scoping ✅ Complete` and append a one-paragraph completion summary (entities added, migration name + applied, Material wired, family-switcher behavior, integration suite count, completion date).

### B. Add to "Locked-in Decisions"

Append:
> - **UI library**: Angular Material (Indigo/Pink theme).
> - **Application layer pattern**: thin — controllers query `AppDbContext` directly. Service layer reserved for non-CRUD logic (parsing, rule engine, transfer matching).
> - **Active family contract**: `X-Family-Id` request header; absent header falls back to the seeded default family (`SeedIds.DefaultFamilyId`); malformed value → 400.

### C. Forward-look at later phases

- **Phase 2 (CSV import)**: schema is ready — `Transaction` and `ImportBatch` tables already exist with the dedup index `(AccountId, SourceRowHash)` and the composite `(FamilyId, AccountId, Date)`. Phase 2 only adds parser, upload/commit endpoints, and the import wizard UI. Note for the Phase 2 planner: `ImportBatch.BankCode` is the parser-registry key — pick the v1 bank's code at execution time.
- **Phase 3 (rules + Ollama)**: `CategoryRule` table exists. Phase 3 will (a) add `Source` and `NeedsReview` columns to `Transaction` via migration, (b) implement the rule engine in Application, (c) extend `OllamaClient` with a chat/generate call. UI for rules is new.
- **Phase 4 (transfers)**: `IsTransfer` and `TransferGroupId` columns already persist. Phase 4 only adds the matcher service + UI badge + unlink action.
- **Phase 5 (dashboard)**: unaffected — query filter handles family scoping for free; aggregation endpoints just `Where(t => !t.IsTransfer)`.
- **Phase 6 (deploy)**: unaffected.
