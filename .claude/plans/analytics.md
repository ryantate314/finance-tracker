# Analytics: Category Pie + Monthly Cash Flow

## Context

Transactatrack currently lets users import, categorize, and browse transactions, but offers no way to see aggregate spending patterns. This plan adds a new `/analytics` page with two charts driven by a shared filter bar:

1. **Expense breakdown pie** — sum of expense transactions grouped by category for the selected date range and accounts. Transfers are excluded; uncategorized transactions appear as their own slice.
2. **Monthly cash-flow chart** — per-month income (positive bars above zero) and expenses (negative bars below zero) with an overlaid net-cash-flow line for the same range.

The filter bar offers presets (This month, Last month, YTD, Last 12 months), a custom date range, and chevron buttons that step the window backward/forward by the preset's own unit.

## Design decisions (confirmed)

- Chart library: **ngx-charts** (`@swimlane/ngx-charts`) — Angular-native, SVG, themes well with Material M3.
- Pie chart: expenses only, exclude transfers, include "Uncategorized" slice.
- Cash-flow chart shares the same date range filter as the pie.
- Cash-flow visual: bars centered on zero (income +, expenses −) + a net line overlay → `<ngx-charts-combo-chart>`.
- Chevrons shift by preset unit (e.g. "This month" ← becomes "April 2026"; YTD ← previous full calendar year; Last 12 months ← prior 12-month window).
- Custom ranges are still available; chevrons shift custom ranges by their own length.

## Backend changes

### New DTOs — `src/Transactatrack.Application/Analytics/`

- `CategoryBreakdownItemDto { Guid? CategoryId, string CategoryName, decimal Amount, int TransactionCount }`
  - `Amount` is the absolute value of summed expenses (positive number for chart display).
  - `CategoryName` is `"Uncategorized"` when `CategoryId == null`.
- `MonthlyCashflowItemDto { int Year, int Month, decimal Income, decimal Expense, decimal Net }`
  - `Income` ≥ 0, `Expense` ≤ 0 (kept signed so the chart can plot directly), `Net = Income + Expense`.

### New controller — `src/Transactatrack.Api/Controllers/AnalyticsController.cs`

Follow `TransactionsController` patterns: inject `AppDbContext` directly; family scoping is automatic via the global query filter; parse `accountIds` as comma-separated GUIDs and `from`/`to` as `yyyy-MM-dd`.

- `GET /api/analytics/category-breakdown?from&to&accountIds`
  - Filter: `Date` in `[from, to]`, optional `AccountId` in list, `IsTransfer == false`, `Amount < 0`.
  - Group by `CategoryId` (null → "Uncategorized"), sum `Amount`, take absolute value, count rows.
  - Order by `Amount` desc.
- `GET /api/analytics/monthly-cashflow?from&to&accountIds`
  - Filter: `Date` in `[from, to]`, optional `AccountId` in list, `IsTransfer == false`.
  - Group by `Date.Year, Date.Month`; `Income = sum where Amount > 0`, `Expense = sum where Amount < 0`.
  - Backfill any missing months in the range server-side so the chart has a continuous x-axis (use `MonthlyCashflowItemDto` with zero income/expense for empty months).
  - Order chronologically.

No new migrations, no new entities. Both endpoints reuse the same family scoping and filtering conventions already in `TransactionsController.cs`.

## Frontend changes

### Package install

```
npm install @swimlane/ngx-charts --save
```
Done from `src/Transactatrack.Web/`. ngx-charts pulls in d3 sub-packages as transitive deps; no extra config needed beyond importing the standalone components in the page.

### New feature folder — `src/Transactatrack.Web/src/app/features/analytics/`

- `analytics.service.ts` — typed HTTP client wrapping the two endpoints. Mirror the shape of `LedgerService` in `src/Transactatrack.Web/src/app/features/ledger/ledger.service.ts` (uses `dateToYmd()`, builds `HttpParams`, returns observables). Export `CategoryBreakdownItem` and `MonthlyCashflowItem` TS interfaces matching the DTOs.
- `analytics-page.ts` — standalone component, signals-based. Reuse the page layout patterns from `ledger-page.ts`:
  - State signals: `range` (the active preset), `from`, `to`, `accountIds`, `pie` (result), `cashflow` (result), `loading`.
  - `accounts` loaded once via `AccountsService.list()`.
  - Effects:
    1. When `range` changes, recompute `from`/`to` via the preset helper (skip when `range === 'custom'`).
    2. When `from`/`to`/`accountIds`/active family change, fire both analytics calls in parallel and update result signals.
  - Filter bar: `mat-button-toggle-group` for presets + two chevron icon-buttons; `mat-form-field` date pickers (visible only when `range === 'custom'`, or shown read-only otherwise so the user can see the resolved window); `mat-select multiple` for accounts (reuse the pattern from ledger page).
  - Charts: `<ngx-charts-advanced-pie-chart>` for the pie, `<ngx-charts-combo-chart>` for the cash-flow chart. Use a Material M3-aligned color scheme (define a `customColors` array or pick one of ngx-charts' built-in schemes).
  - Render currency totals in tooltips using the existing `| number:'1.2-2'` pattern.
- `analytics-page.html` and `analytics-page.scss` (or inline if compact) — match the look of `ledger-page` (toolbar-style filter row at top, cards below).
- `time-range.ts` (small helper module): pure functions
  - `presetRange(preset, today): { from: Date, to: Date }` for `'thisMonth' | 'lastMonth' | 'ytd' | 'last12Months'`.
  - `shiftRange(preset, current, direction): { from: Date, to: Date }` — handles preset-aware stepping (month for `thisMonth`/`lastMonth`, year for `ytd`, 12 months for `last12Months`, custom-length for `custom`).
  - Keep this in the analytics folder; not worth promoting to shared utils until a second caller appears.

### Wiring

- `src/Transactatrack.Web/src/app/app.routes.ts` — add `{ path: 'analytics', loadComponent: () => import('./features/analytics/analytics-page').then(m => m.AnalyticsPage) }`.
- `src/Transactatrack.Web/src/app/app.html` — add `<a mat-button routerLink="/analytics" routerLinkActive="active-link">Analytics</a>` next to the existing nav links.
- No changes needed to `familyIdInterceptor` — it already injects `X-Family-Id` on all `apiBaseUrl` requests.

## Edge cases to handle

- **Empty result sets**: render an empty-state message inside each chart card when the API returns no rows.
- **Future-dated transactions**: `presetRange('thisMonth')` uses 1st-of-month → today; future-dated rows are excluded naturally.
- **Months with no data inside the selected range**: the API backfills zero-rows so the cash-flow x-axis stays continuous.
- **Active family switch**: existing `familyContext.activeFamilyId()` signal is already part of effect dependencies via the interceptor — recompute results on family change like `ledger-page` does.
- **Chevron at custom range**: shift `from` and `to` backward/forward by `(to - from + 1 day)`.

## Verification

1. `make db-update` (if any pending migrations) — none expected from this change.
2. `make api` and `make ui` in separate terminals.
3. Browse to `http://localhost:4200/analytics`:
   - Pie shows expense categories for "This month" by default; Uncategorized appears if any expenses are uncategorized; transfers absent.
   - Cash-flow chart shows current month bar + net line at the same time window.
   - Cycle presets — both charts update; chevrons step backward/forward by preset unit; custom range shows date pickers.
   - Account multi-select narrows both charts.
   - Switch family in toolbar — both charts reload empty/with that family's data.
4. Sanity-check the API directly via the transactatrack-api skill against the same ranges to confirm totals match what the UI shows (sum of pie slices == sum of expense bars over same window).
5. Existing tests untouched; no new integration tests in this pass (charts are presentational; aggregation logic is straightforward EF group-by — if regressions surface, add focused tests for `AnalyticsController` later).
