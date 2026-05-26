# Plan: System Income category + Kind-based cashflow

## Context

The monthly cashflow chart currently classifies transactions as income vs expense purely by sign (`Amount > 0` vs `Amount < 0`). That's wrong in two directions:
- A refund on an expense shows up as "income" for the month even though it's really a negative-expense.
- An expense that arrives as a positive amount (e.g. a misformatted CSV row, a credit-card adjustment) ends up in the income bucket.

We just shipped a parallel design for transfers: a `CategoryKind` enum, a system category (`Transfer`) the user can't delete or rename, and analytics that key off `Kind` rather than name. Extending that pattern to `Income` is the natural fix: anything categorized as Income (or a sub of Income) counts as income on the chart; everything else (excluding transfers) is expense.

## Approach

### 1. Domain

`src/Transactatrack.Domain/Enums/CategoryKind.cs` — add `Income = 2`. No other entity change; `Category.Kind` already exists.

### 2. EF migration

`make migrate name=AddIncomeSystemCategory`. The `Kind` column already exists from `AddCategoryKindAndSeedTransfer`, so this migration is data-only:

```sql
UPDATE "Categories" SET "Kind" = 'Income'
  WHERE LOWER("Name") = 'income' AND "Kind" = 'User';

INSERT INTO "Categories" ("Id", "FamilyId", "CreatedUtc", "Name", "Kind")
SELECT gen_random_uuid(), f."Id", (NOW() AT TIME ZONE 'UTC'), 'Income', 'Income'
FROM "Families" f
WHERE NOT EXISTS (
  SELECT 1 FROM "Categories" c
  WHERE c."FamilyId" = f."Id" AND c."Kind" = 'Income'
);
```

No transaction backfill — unlike Transfer, Income doesn't denormalize state onto the row; analytics joins to `Categories.Kind` at query time.

The user's existing Effler / Effler-Tate families already have user-created "Income" categories with Paycheck / Interest / Other Income sub-categories — the migration's `LOWER("Name") = 'income'` clause promotes them in place, so the sub-categories come along for free.

### 3. API: family creation seeds Income too

`src/Transactatrack.Api/Controllers/FamiliesController.cs` — in `Create`, after seeding the Transfer category, also seed an Income one. Same pattern.

### 4. API: cashflow query keys off Kind

`src/Transactatrack.Api/Controllers/AnalyticsController.cs` (`MonthlyCashflow`, ~line 54–95). Replace the sign-based split with a `Kind`-based join.

Current:
```csharp
Income  = g.Where(t => t.Amount > 0).Sum(...) ?? 0m,
Expense = g.Where(t => t.Amount < 0).Sum(...) ?? 0m
```

Proposed: build the query as a join to `Categories`, then bucket by `c.Kind == CategoryKind.Income`:
- **Income bucket**: sum of signed `Amount` where the joined category's `Kind == Income`. Normally positive, but a refund-of-income (negative) nets correctly.
- **Expense bucket**: sum of signed `Amount` for everything else not-transfer-not-income — including uncategorized rows. Normally negative; refunds within an expense category net here too (mirrors the breakdown change we just shipped).
- **Net** stays `income + expense`.

The DTO shape (`MonthlyCashflowItemDto(Year, Month, Income, Expense, Net)`) is unchanged. The Angular cashflow chart already takes income as a positive bar and expense as a negative bar — no UI change required.

### 5. API: protect-against-rename/delete already in place

`CategoriesController` already returns 409 for any `Kind != User`, so the new Income system category is automatically locked. No controller change.

### 6. LLM: special-case the filter

`src/Transactatrack.Infrastructure/Categorization/CategorizationService.cs:108` currently filters to `c.Kind == CategoryKind.User`. Change to `c.Kind != CategoryKind.Transfer`, so the LLM can still suggest Income (and its Paycheck / Interest subs) but cannot suggest Transfer.

Rationale: Transfer detection genuinely needs cross-account context the LLM doesn't have; income detection from descriptions (PAYROLL, DEPOSIT, INTEREST PAYMENT) is well within reach.

### 7. UI

`src/Transactatrack.Web/src/app/features/categories/categories.service.ts` — extend `CategoryKind`:
```ts
export type CategoryKind = 'User' | 'Transfer' | 'Income';
```

`categories-page.ts` already hides rename/delete buttons whenever `cat.kind !== 'User'` and shows the lock badge — Income inherits the protection automatically.

No analytics-page change needed; it consumes the DTO shape that didn't change.

### 8. Tests

In `tests/Transactatrack.IntegrationTests`:

- Extend `SystemTransferCategoryTests.CreatingFamily_AutoSeedsTransferCategory`: a fresh family should now have **both** Transfer and Income system categories. Adjust the existing assert from `Single(categories)` to `categories.Count == 2`, with assertions on both kinds.
- Update `FamilyScopingTests.Categories_CanCreateSubCategoryAndList` similarly — a freshly created family now has 2 seeded categories before the user adds "Food", so `Equal(3, categories.Count)`.
- New `IncomeSystemCategoryTests`:
  - Deleting / renaming the Income system category → 409.
  - Cashflow chart: create txns of mixed sign, some categorized Income, some Uncategorized, some in a user expense category. Assert that:
    - Income bucket = sum of Income-categorized signed amounts (independent of sign).
    - Expense bucket = sum of the rest (excluding transfers, including uncategorized).
    - Net = Income + Expense.

### 9. Phased plan note

`.claude/plans/phased-development-plan.md` — in the Phase 5 section (Spending Dashboard), mention that the Income/Transfer system-kind pattern is the canonical hook for category-driven aggregation; future report types should use `Category.Kind` rather than re-inventing rules.

## Critical files

- `src/Transactatrack.Domain/Enums/CategoryKind.cs`
- `src/Transactatrack.Infrastructure/Persistence/Migrations/<new>_AddIncomeSystemCategory.cs`
- `src/Transactatrack.Api/Controllers/FamiliesController.cs`
- `src/Transactatrack.Api/Controllers/AnalyticsController.cs` (`MonthlyCashflow`)
- `src/Transactatrack.Infrastructure/Categorization/CategorizationService.cs` (LLM filter)
- `src/Transactatrack.Web/src/app/features/categories/categories.service.ts`
- `tests/Transactatrack.IntegrationTests/Categorization/SystemTransferCategoryTests.cs` (assert count update)
- `tests/Transactatrack.IntegrationTests/FamilyScopingTests.cs` (assert count update)
- `tests/Transactatrack.IntegrationTests/Categorization/IncomeSystemCategoryTests.cs` (new)

## Verification

1. `make db-update && make test` — all green.
2. Inspect existing Effler / Effler-Tate families: `GET /api/categories` should show their existing "Income" category now has `kind: 'Income'`; no duplicate was inserted.
3. Hit `GET /api/analytics/monthly-cashflow?from=...&to=...` for a month with both a paycheck and a paycheck refund (if any) — verify the income bucket nets correctly.
4. Hit the same endpoint for a month with positive-sign expense adjustments (rare, but real) — verify those now sit in the expense bucket instead of inflating income.
5. Try to delete or rename the Income category in the UI → button hidden / API returns 409.
6. Import a CSV containing a paycheck row in a fresh family → confirm the LLM suggests Income (or a sub of it).

## Behavior changes worth flagging to the user up front

- **Uncategorized positive amounts** previously counted as income; they now count as expense (signed-sum into the expense bucket). For typical use this is rare and harmless, but if a family has lots of uncategorized deposits this would visibly shift the chart until those rows are categorized.
- The chart's "income" line is now driven by **manual categorization quality**: an unclassified paycheck no longer shows as income until tagged. This is a feature (consistent semantics) but worth noting.
