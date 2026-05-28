# Family JSON Import

## Context

Inverse of the just-built export feature ([family-json-export.md](family-json-export.md)). The user wants to take a Family export JSON produced by a lower environment and re-hydrate it in an upper environment. Two modes:

- **Import as new family** — generates a fresh `FamilyId`, brings everything else in with original Ids preserved.
- **Merge into existing family** — additive merge keyed by GUID; if an entity's `Id` already exists in the target family, skip it; otherwise insert it under the target `FamilyId`. Non-User categories (Transfer / Income) get remapped to the target family's existing system categories so we don't end up with duplicates.

Flow is **one-shot atomic** — single POST, single DB transaction, summary in the response. No staging / preview step.

UI entry point: a new **Import** header button on the Families page (next to **New**) opens a dialog covering both modes.

## Backend

### 1. AppDbContext: opt-out of auto-stamping

`src/Transactatrack.Infrastructure/Persistence/AppDbContext.cs`, around lines 40–55, unconditionally overwrites `CreatedUtc` on every `Added` row (and for `FamilyScopedEntity`, stamps `FamilyId` when it's `Guid.Empty`). For import we must preserve both.

Add a public toggle and gate the stamping blocks on it:

```csharp
public bool SuppressAutoStamping { get; set; }
// ...
if (!SuppressAutoStamping)
{
    foreach (var entry in ChangeTracker.Entries<FamilyScopedEntity>().Where(...)) { ... }
    foreach (var entry in ChangeTracker.Entries<Family>().Where(...)) { ... }
}
```

The import service sets `_db.SuppressAutoStamping = true` for the duration of its work, then restores it. No other call sites need to change — the default is `false`.

### 2. New summary DTO

`src/Transactatrack.Application/Families/FamilyImportSummaryDto.cs`:

```csharp
public record FamilyImportSummaryDto(
    Guid FamilyId,
    string FamilyName,
    int OwnersInserted,         int OwnersSkipped,
    int AccountsInserted,       int AccountsSkipped,
    int CategoriesInserted,     int CategoriesSkipped,
    int CategoriesRemapped,     // non-User cats mapped onto target's system cats
    int SubCategoriesInserted,  int SubCategoriesSkipped,
    int CategoryRulesInserted,  int CategoryRulesSkipped,
    int ImportBatchesInserted,  int ImportBatchesSkipped,
    int TransactionsInserted,   int TransactionsSkipped
);
```

For "import as new family", every `*Skipped` count is 0 and `CategoriesRemapped` is 0 — but the same DTO is fine for both modes.

### 3. Service

`src/Transactatrack.Application/Families/IFamilyImportService.cs`:

```csharp
public interface IFamilyImportService
{
    Task<FamilyImportSummaryDto> ImportAsNewAsync(FamilyExportDto export, string? nameOverride, CancellationToken ct);
    Task<FamilyImportSummaryDto> MergeAsync(Guid targetFamilyId, FamilyExportDto export, CancellationToken ct);
}
```

Implementation at `src/Transactatrack.Infrastructure/Families/FamilyImportService.cs`. Register as `Scoped` in `Program.cs` alongside the other application services.

#### ImportAsNewAsync

1. `Guid newFamilyId = Guid.NewGuid();` (don't reuse `export.Family.Id` — collisions defeat the purpose of "new family").
2. `_db.SuppressAutoStamping = true;` inside a `try { ... } finally { _db.SuppressAutoStamping = false; }`.
3. `await using var dbTx = await _db.Database.BeginTransactionAsync(ct);` (matches the pattern in `ImportService.UploadAsync` line ~94).
4. Insert `Family` directly via `AppDbContext` (do NOT call `FamiliesController.Create` — that would auto-seed Transfer + Income categories that conflict with the imported ones). Use `Name = nameOverride ?? export.Family.Name`; `CreatedUtc = DateTime.UtcNow` (new family, new timestamp).
5. Walk each collection in FK order, projecting export DTOs back to entities with `FamilyId = newFamilyId`, all other Ids and `CreatedUtc` preserved from the DTO. Order: `Owners → Categories → SubCategories → Accounts → CategoryRules → ImportBatches → Transactions`.
6. `SaveChangesAsync` + commit.
7. Return summary with all `*Inserted` = source-collection counts, `*Skipped` = 0.

#### MergeAsync

1. Verify target family exists; otherwise return null / throw (controller maps to 404).
2. Same `SuppressAutoStamping` + transaction setup.
3. **Pre-load existing target Ids** in one batch (use `IgnoreQueryFilters()` and filter manually by `FamilyId == targetFamilyId` — same trick as the export endpoint):
   - `HashSet<Guid>` for owners, accounts, categories, subcategories, rules, batches, transactions.
   - `Dictionary<CategoryKind, Guid>` for target's non-User categories (`Transfer`, `Income`).
   - `HashSet<(Guid AccountId, string SourceRowHash)>` for transactions (matches the unique index in `TransactionConfiguration.cs:26`).
4. **Categories pass — build remap dictionary**:
   ```csharp
   var categoryRemap = new Dictionary<Guid, Guid>();   // exported.Id → target.Id
   foreach (var c in export.Categories)
   {
       if (c.Kind != CategoryKind.User &&
           targetSystemCategoryByKind.TryGetValue(c.Kind, out var existingId))
       {
           categoryRemap[c.Id] = existingId;
           categoriesRemapped++;
           continue;
       }
       if (existingCategoryIds.Contains(c.Id)) { categoriesSkipped++; continue; }
       // insert with FamilyId = targetFamilyId, preserve everything else
       categoryRemap[c.Id] = c.Id;
       categoriesInserted++;
   }
   ```
   The remap dict is used to rewrite `CategoryId` on Transactions, `CategoryId` on SubCategories, and `TargetCategoryId` on CategoryRules.
5. **SubCategories pass** — for each exported subcategory: skip if Id already in target; otherwise insert with `CategoryId = categoryRemap[s.CategoryId]` (always present, since every SubCategory's parent was visited in step 4). Build a parallel `subCategoryRemap` only if Kind=Transfer or Income SubCategories exist in the source — but in practice this is unused; default to identity mapping.
6. **Owners / Accounts / CategoryRules / ImportBatches passes** — straightforward skip-on-Id-conflict, insert with `FamilyId = targetFamilyId`. For `CategoryRule`, rewrite `TargetCategoryId` via `categoryRemap`; if `TargetSubCategoryId` is set, look it up in `subCategoryRemap`.
7. **Transactions pass** — skip if `Id` exists in target OR `(AccountId, SourceRowHash)` already present (prevents the unique-index violation). Insert remaining with `FamilyId = targetFamilyId` and `CategoryId` / `SubCategoryId` remapped where applicable.
8. `SaveChangesAsync` + commit.

### 4. Controller endpoints

Add to `FamiliesController` (which already houses Export). Body is the raw `FamilyExportDto` JSON, sent with `Content-Type: application/json` — no multipart wrapper, since the browser can post a `Blob` directly:

```csharp
[HttpPost("import")]
[RequestSizeLimit(50 * 1024 * 1024)]
public async Task<ActionResult<FamilyImportSummaryDto>> ImportNew(
    [FromBody] FamilyExportDto export,
    [FromQuery] string? name,
    CancellationToken ct)
{
    var error = ValidateExport(export);
    if (error is not null) return BadRequest(new { title = error, status = 400 });
    var summary = await _importService.ImportAsNewAsync(export, name, ct);
    return Ok(summary);
}

[HttpPost("{id:guid}/import")]
[RequestSizeLimit(50 * 1024 * 1024)]
public async Task<ActionResult<FamilyImportSummaryDto>> Merge(
    Guid id, [FromBody] FamilyExportDto export, CancellationToken ct)
{
    var error = ValidateExport(export);
    if (error is not null) return BadRequest(new { title = error, status = 400 });
    if (!await _db.Families.AnyAsync(f => f.Id == id, ct)) return NotFound();
    var summary = await _importService.MergeAsync(id, export, ct);
    return Ok(summary);
}
```

`ValidateExport`: null check, `ExportVersion == 1`, non-null `Family` and lists. Treat unknown versions as a client error.

Both routes are prefix-exempt by `FamilyContextMiddleware._unscopedPrefixes` (line 7) — no `X-Family-Id` required.

### 5. DI wiring

In `Program.cs`, alongside the existing `AddScoped<IImportService, ImportService>()`:

```csharp
builder.Services.AddScoped<IFamilyImportService, FamilyImportService>();
```

## Frontend

### 1. FamiliesService

Add to `src/Transactatrack.Web/src/app/features/families/families.service.ts`:

```ts
importAsNew(file: Blob, name?: string) {
  const url = name ? `${this.base}/import?name=${encodeURIComponent(name)}` : `${this.base}/import`;
  return this.http.post<FamilyImportSummaryDto>(url, file, {
    headers: { 'Content-Type': 'application/json' }
  });
}

mergeInto(targetFamilyId: string, file: Blob) {
  return this.http.post<FamilyImportSummaryDto>(`${this.base}/${targetFamilyId}/import`, file, {
    headers: { 'Content-Type': 'application/json' }
  });
}
```

`FamilyImportSummaryDto` TypeScript interface lives in the same file.

The `familyIdInterceptor` will still attach `X-Family-Id` (its skip rule matches only the exact `/api/families` URL); harmless because the endpoint is unscoped server-side.

### 2. Import dialog

New component `src/Transactatrack.Web/src/app/features/families/family-import-dialog.ts`. Mirrors the pattern of `family-edit-dialog.ts`. Contents:

- File input (`<input type="file" accept="application/json">`); stores the selected `File` blob in a signal.
- `mat-radio-group`: **Import as new family** / **Merge into existing family**.
- If "new": optional `<input matInput>` for name override.
- If "merge": `<mat-select>` listing all families from `FamiliesService.families$`.
- Submit button — disabled until file + mode (and target if merge) are set.
- On submit: call the service; on success, show the returned summary in a follow-up `MatDialog` (simple "X owners added, Y categories skipped, …" panel with **Close**); on error, surface via `extractErrorMessage` + `MatSnackBar` like the existing edit/delete flows.

The summary panel can be a second small component or just an inline section that replaces the form once the response arrives — the second is simpler. After Close, the dialog closes with a "did import" signal so the host page can refresh.

### 3. Families page header

Edit `src/Transactatrack.Web/src/app/features/families/families-list.ts`. Add an Import button next to **New**:

```html
<div class="page-header">
  <h2>Families</h2>
  <span>
    <button mat-stroked-button (click)="openImport()"><mat-icon>upload</mat-icon> Import</button>
    <button mat-flat-button (click)="openNew()">New</button>
  </span>
</div>
```

`openImport()` opens `FamilyImportDialog`, and on close refreshes the family list (so a newly-imported family appears immediately).

## Verification

1. `make api` and `make ui`.
2. **Round-trip (import as new):**
   a. Export a family from the Families page (the existing download button).
   b. Click **Import** → pick the file → **Import as new family** → submit.
   c. Confirm summary shows non-zero `OwnersInserted`, `AccountsInserted`, etc.
   d. Switch to the new family in the toolbar; confirm Accounts, Categories, Ledger pages show the imported data.
   e. Confirm Transfer / Income categories appear exactly once (no duplicates from FamiliesController auto-seed).
3. **Merge — clean target:**
   a. Create a fresh family in the UI (so it auto-seeds Transfer + Income with new GUIDs).
   b. Export the populated family from step 2.
   c. **Import** → pick file → **Merge into [fresh family]**.
   d. Verify summary: `CategoriesRemapped == 2` (Transfer + Income), no `CategoriesSkipped` for system kinds, all transactions inserted, no duplicate Transfer/Income categories in target.
4. **Merge — overlapping target:**
   a. Re-merge the same file into the same target.
   b. Verify summary: most counts now in the `*Skipped` columns, `TransactionsInserted == 0`.
5. **Error cases (curl):**
   - `POST /api/families/import` with `{}` → 400 with version message.
   - `POST /api/families/import` with `ExportVersion=999` → 400.
   - `POST /api/families/<missing>/import` with valid body → 404.
   - Body > 50 MB → 413.
6. **CreatedUtc preservation** — pick a transaction in the source export, note its `createdUtc`, find it in the target DB after import-as-new, verify the timestamp matches exactly (proves `SuppressAutoStamping` is working). Repeat for a Family CreatedUtc? Skip — the new Family's CreatedUtc is intentionally `DateTime.UtcNow`.
7. **Tests** — run `make test-unit` and `make test-integration` (the latter requires Docker). Consider adding an integration test that performs an export → drop family → import-as-new and asserts entity counts match.
