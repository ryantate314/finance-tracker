# Tech Lead Review — Phase 1 Core Domain

**Date:** 2026-05-05
**Scope:** Uncommitted changes on `main` introducing Phase 1 (Domain/Application/Infrastructure/Api/Web/e2e).

## Summary

A solid Phase 1 vertical slice: clean architecture layering (Domain/Application/Infrastructure/Api), a multi-tenant `FamilyId` model with EF query filters, full CRUD for Families/Owners/Accounts/Categories/SubCategories, an Angular shell with Material UI feature pages, integration tests via Testcontainers, and a Playwright e2e flow. The bones are good. There is one **build-breaking** regression and several issues worth addressing before merge.

---

## Blockers

### 1. Integration tests do not compile (`tests/Transactatrack.IntegrationTests/FamilyScopingTests.cs`)
A `dotnet build` of the test project fails with 4 errors:
- `FamilyScopingTests.cs:100,103` — `new CreateCategoryRequest(null, "Food")` and `new CreateCategoryRequest(parent!.Id, "Groceries")` use a 2-arg constructor that doesn't exist; `CreateCategoryRequest` only takes `Name`.
- `FamilyScopingTests.cs:110,111` — assertions reference `CategoryDto.ParentId`, but `CategoryDto` exposes `SubCategories`, not `ParentId`.

This is the fingerprint of a mid-stream design switch. The first migration (`Phase1Schema`) created `Categories.ParentId` as a self-reference. The second migration (`Phase1SubCategories`) drops `ParentId` and introduces a separate `SubCategories` table. The DTOs/controllers/Angular were updated, but `FamilyScopingTests.Categories_CanCreateChildAndList_FlatWithParentId` was not. **Rewrite that test against the new sub-category endpoint** (`POST /api/categories/{id}/subcategories`) before merging — otherwise CI will be red the moment it's wired up.

### 2. Two migrations for one schema
`20260505180337_Phase1Schema` creates `ParentId` and then `20260505185334_Phase1SubCategories` removes it ~7 minutes later. Since this is pre-release, **squash these into a single migration** so production never executes the throw-away `ParentId` column. Otherwise you're shipping schema churn that future operators have to wade through.

---

## Significant issues

### 3. Default-family fallback masks bugs and is a footgun (`FamilyContextMiddleware.cs:32-35`)
When `X-Family-Id` is absent, the middleware silently falls back to `SeedIds.DefaultFamilyId`. Any controller/page that forgets to send the header will read/write the default family with no error. Recommendation: require the header on every endpoint outside the families-listing endpoints (return 400), or at minimum log a warning. The current Angular interceptor *almost* always sends it, so fail-closed is cheap.

### 4. `FamilyContextService` localStorage usage breaks SSR (`family-context.service.ts:8`)
`localStorage.getItem(...)` runs at field initialization. If you ever turn on Angular SSR (or run `ng test` in a non-DOM env), this will throw at injection time. Wrap with a `typeof window !== 'undefined'` check, or use a platform-aware abstraction.

### 5. `FamilyContextService` doesn't validate stored ID against current families
On boot, `app.ts:25-31` recovers a stored family ID and uses it if it's still in the list — good. But if it's stale, you fall back to default. Fine. However the interceptor will happily send a stale ID for one request before the families list arrives, racing the bootstrap. If no family is selected yet (`null`), the interceptor passes through with no header, so the API silently uses the default family. Consider gating non-family API calls until the bootstrap resolves.

### 6. Hard-coded password in `appsettings.Development.json`
`Passw0rd!` is checked into source control. Even for local dev I'd push these into user-secrets or `.env` and keep only the `.example` file in the repo.

### 7. CORS default origin (`Program.cs:34-37`)
`WithOrigins("http://localhost:4200")` is hard-coded. Fine for dev, but consider pulling from configuration so a developer running on a different port doesn't have to recompile.

---

## Minor / nits

### 8. `CreateAccountRequest` and `UpdateAccountRequest` are identical (Application/Accounts/*.cs)
Two records with the same six fields. If they diverge later (e.g., owner immutable on update) this pays off; otherwise consolidate to one DTO.

### 9. `AccountsController.Create` validation is partial
Validates `OwnerId` exists in active family (good), but doesn't reject `FamilyId` mismatches — and there's no equivalent guard on `Update`. The query filter on `Owners` covers it implicitly; good. But on `CategoryRules` you'll need similar checks for `TargetCategoryId` and `AccountId` because cross-family references would slip past EF's restrict-on-FK.

### 10. `CategoriesController.CreateSub` returns Location of the parent (`CategoriesController.cs:90-91`)
`CreatedAtAction(nameof(Get), new { id = categoryId }, ...)` — Location header points to the parent category, not the sub-category. Either expose a sub-category GET or return `Created(string.Empty, dto)` to be honest.

### 11. `AccountType` enum has no explicit values (`AccountType.cs`)
Stored as ints. Adding a new value in the middle (or reordering) silently corrupts data. Either pin explicit values (`Checking = 1, ...`) or persist as strings.

### 12. `interceptor` URL match is fragile (`family-id.interceptor.ts:8`)
```ts
if (req.url.endsWith('/api/families') || req.url.includes('/api/families?')) return next(req);
```
This excludes list/create on families, but `/api/families/{id}` GET/PUT/DELETE still send the header — harmless, but inconsistent. Worth a one-line comment explaining intent. Also: `endsWith('/api/families')` would also match `/api/familiestypos` if your URL ever changed; consider an explicit regex or path comparison.

### 13. `extractErrorMessage` swallows useful detail (`api-error.ts`)
Returns `'An error occurred'` whenever the body lacks `title`, including network errors where `error.message` would be more useful. Consider falling back to `error.message`/`status` info.

### 14. `families$.subscribe` in `App.ngOnInit` has no unsubscribe (`app.ts:25-31`)
Root component, so it lives the whole app, but it's a smell. Use `takeUntilDestroyed()` or `toSignal` patterns and react via `effect()`.

### 15. `IntegrationTestFactory.DisposeAsync` is not awaited deterministically
`PostgreSqlContainer` lives for the lifetime of the `IClassFixture` and stops on dispose — fine. But there's no DB reset between tests; tests rely on unique family names. Today that works; the moment someone adds a test that doesn't isolate by family, it'll flake. Consider a `Respawn`-style cleanup or a per-test transaction wrapper.

### 16. `Application` project's `csproj` is untracked
The new `Transactatrack.Application/Transactatrack.Application.csproj` is in the working tree but the diff showed only `Infrastructure.csproj`. Make sure this is staged — and likewise that the project is added to the `.sln` file.

### 17. `tests/e2e/playwright.config.ts` has `fullyParallel: false` and no `webServer`
The e2e expects you to manually run the API + Angular. Consider adding `webServer` entries so `npm test` is one command, and make `fullyParallel: true` once tests are isolated.

### 18. Plan file checked in
Make sure `.claude/plans/phase-1-core-domain.md` is intentionally checked in (it's untracked).

---

## Positives worth keeping

- `FamilyScopedEntity` + global query filters + `SaveChangesAsync` override = correct, idiomatic multi-tenancy. Filter expressions reference an injected dependency, so EF re-evaluates them per query rather than baking the value into the cached model.
- `Restrict` on all FKs to `Family` — no accidental cascading family deletes.
- Cascade only on `SubCategory → Category` is the right call (matches the UI's parent/child edit affordance).
- Unique index `Transactions (AccountId, SourceRowHash)` — good defense against double-imports.
- DTOs as records, controllers thin and consistent across resources, `DbUpdateException → 409` pattern is uniform.
- Health controller runs DB and Ollama checks in parallel and degrades gracefully on failure.
- Integration tests use real Postgres via Testcontainers, not mocks.

---

## Recommended next steps before merging

1. Fix the four compile errors in `FamilyScopingTests.cs` (rewrite the categories test against `POST /api/categories/{id}/subcategories`).
2. Squash the two Phase 1 migrations.
3. Decide policy on missing `X-Family-Id` (fail-closed vs. default) and document it.
4. Move `Passw0rd!` out of `appsettings.Development.json`.
5. Confirm `Transactatrack.Application.csproj` is staged and added to the solution.
