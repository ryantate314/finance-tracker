# Family JSON Export

## Context

We run transactatrack in multiple environments. To promote data from a lower env to an upper env, we need a way to dump a complete `Family` snapshot to disk. This plan covers **export only** — the user has explicitly deferred the re-import side.

The export must include everything family-scoped (per chosen scope: "Full family snapshot"): `Family`, `Owners`, `Accounts`, `Categories` (with `SubCategories`), `CategoryRules`, `ImportBatches`, and `Transactions`. Original `Id` and `CreatedUtc` values are preserved as-is so a future importer can choose whether to keep or remap them.

Outcome: clicking the new download icon on a row in the Families page produces a single `.json` file containing the entire family's data, downloaded by the browser.

## Backend (.NET)

### 1. New DTO: `FamilyExportDto`

Add `src/Transactatrack.Application/Families/FamilyExportDto.cs` as an `init`-style record. It composes existing DTOs — no new field shapes:

```
public sealed record FamilyExportDto(
    int ExportVersion,                       // start at 1; bump on breaking shape changes
    DateTime ExportedUtc,
    FamilyDto Family,
    IReadOnlyList<OwnerDto> Owners,
    IReadOnlyList<AccountDto> Accounts,
    IReadOnlyList<CategoryDto> Categories,   // already nests SubCategoryDto
    IReadOnlyList<CategoryRuleDto> CategoryRules,
    IReadOnlyList<ImportBatchDto> ImportBatches,
    IReadOnlyList<TransactionDto> Transactions);
```

Reuses: `FamilyDto`, `OwnerDto`, `AccountDto`, `CategoryDto` (nests `SubCategoryDto`), `CategoryRuleDto`, `ImportBatchDto`, `TransactionDto`. No new entity-to-DTO projection logic needed beyond what is already used in the corresponding controllers.

### 2. New endpoint: `GET /api/families/{id}/export`

Add to `src/Transactatrack.Api/Controllers/FamiliesController.cs`.

Implementation notes:

- The path prefix `/api/families` is already exempted in `FamilyContextMiddleware._unscopedPrefixes` (line 7), so this endpoint does not require `X-Family-Id` and `_familyContext.ActiveFamilyId` may be `Guid.Empty`.
- All reads MUST use `.IgnoreQueryFilters()` and filter manually by the path `id`, otherwise the global query filter (`AppDbContext.OnModelCreating`, lines 31–37) would drop everything when no active family is set.
- 404 if no `Family` with that id exists.
- Build the DTO by running one filtered query per entity type, then project to DTOs using the same shape the existing list endpoints use (cross-reference `AccountsController.List`, `CategoriesController.List`, `TransactionsController.List`, `ImportsController` listing endpoint, `CategoryRulesController` listing endpoint). For `Categories`, eagerly load `SubCategories` and nest in the DTO the same way `CategoriesController` does.
- For `Transactions`, do NOT apply the `Status == Committed` filter that the ledger uses — the snapshot must include `Pending` and `Discarded` batches too, otherwise their referenced `ImportBatch` rows would have no corresponding transactions on re-import.
- Return as a downloadable file:

```
var json = JsonSerializer.Serialize(dto, _jsonOptions);
var bytes = Encoding.UTF8.GetBytes(json);
var slug = Slug(family.Name);                           // [a-zA-Z0-9-]+, fallback "family"
var filename = $"transactatrack-{slug}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
return File(bytes, "application/json", filename);       // sets Content-Disposition
```

Use the same `JsonSerializerOptions` the API is configured with (PropertyNamingPolicy + `JsonStringEnumConverter` from `Program.cs`) — inject `IOptions<JsonOptions>` and pass `.SerializerOptions`, so the export matches API wire format exactly (enum strings, camelCase). This guarantees round-trip parity if/when an importer is built.

A small private `Slug(string name)` helper inside the controller is fine — keep it co-located, ~5 lines.

### 3. No migration, no new service class

This is read-only, fits the "controllers inject `AppDbContext` directly" convention. No `Transactatrack.Application` service is warranted.

## Frontend (Angular)

### 1. `FamiliesService.exportFamily(id)`

File: `src/Transactatrack.Web/src/app/features/families/families.service.ts`.

Add a method that returns the full `HttpResponse<Blob>` so the component can read the `Content-Disposition` filename:

```
exportFamily(id: string) {
  return this.http.get(`${this.base}/${id}/export`, {
    responseType: 'blob',
    observe: 'response',
  });
}
```

The `familyIdInterceptor` (`src/app/core/family-context/family-id.interceptor.ts`) will still add `X-Family-Id` to this request because its skip rule only matches the exact `/api/families` URL — that is harmless since the server endpoint is unscoped anyway. No interceptor change needed.

### 2. Download icon in the Families table

File: `src/Transactatrack.Web/src/app/features/families/families-list.ts`.

Add a third icon button in the `actions` column, before edit/delete, matching the existing `mat-icon-button` + `aria-label` pattern:

```html
<button mat-icon-button (click)="exportFamily(f)" aria-label="Export"><mat-icon>download</mat-icon></button>
```

Add the component method:

```ts
exportFamily(f: FamilyDto) {
  this.svc.exportFamily(f.id).subscribe(res => {
    const blob = res.body!;
    const filename = parseFilename(res.headers.get('Content-Disposition')) ?? `${f.name}.json`;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename; a.click();
    URL.revokeObjectURL(url);
  });
}
```

`parseFilename` is a tiny local helper (regex on `filename="..."`). Keep it inline in the component file — no shared util needed, this is the only download in the app.

## Verification

1. `make db-update && make api` and `make ui`.
2. In the UI: create a family with at least one owner, account, category+subcategory, category rule, and import a CSV batch with several transactions (some categorized, some `NeedsReview`).
3. Click the download icon on that family's row. Verify a `transactatrack-<slug>-<timestamp>.json` file is saved.
4. Open the JSON and confirm:
   - Top-level fields: `exportVersion`, `exportedUtc`, `family`, `owners`, `accounts`, `categories`, `categoryRules`, `importBatches`, `transactions`.
   - Counts match what the UI shows for that family.
   - Enums serialize as strings (e.g., `"Checking"`, `"Committed"`), not integers.
   - GUIDs and `createdUtc` timestamps are preserved (compare a couple against the DB).
   - `categories[].subCategories` is populated.
5. Switch the active family in the toolbar to a *different* family, then export the original family from the row. Confirm the export still contains the row's family's data — proves the endpoint is not affected by `X-Family-Id`.
6. `curl -i http://localhost:5080/api/families/<missing-guid>/export` returns 404.
7. `curl -OJ http://localhost:5080/api/families/<real-guid>/export` (no `X-Family-Id` header) downloads the file successfully — proves the middleware-exempt path works.
