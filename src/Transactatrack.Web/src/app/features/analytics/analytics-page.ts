import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { Color, LegendPosition, NgxChartsModule, ScaleType } from '@swimlane/ngx-charts';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamilyContextService } from '../../core/family-context/family-context.service';
import { AccountDto, AccountsService } from '../accounts/accounts.service';
import { OwnerDto, OwnersService } from '../owners/owners.service';
import { AnalyticsService, CategoryBreakdownItem, MonthlyCashflowItem, SankeyData } from './analytics.service';
import { SankeyChartComponent } from './sankey-chart.component';
import { DateRange, RangePreset, formatRangeLabel, presetRange, shiftRange } from './time-range';

interface PieDatum { name: string; value: number; }
interface BreakdownRow { categoryId: string | null; name: string; amount: number; count: number; pct: number; }
interface BarSeriesPoint { name: string; value: number; }
interface BarGroup { name: string; series: BarSeriesPoint[]; }
interface LineSeriesPoint { name: string; value: number; }
interface LineSeries { name: string; series: LineSeriesPoint[]; }

@Component({
  selector: 'app-analytics-page',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule,
    NgxChartsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    DecimalPipe,
    SankeyChartComponent,
  ],
  template: `
    <div class="page-header">
      <h2>Analytics</h2>
      <span class="muted">{{ rangeLabel() }}</span>
    </div>

    <div class="filters">
      <mat-button-toggle-group [value]="preset()" (change)="onPresetChange($event.value)" hideSingleSelectionIndicator="true">
        <mat-button-toggle value="thisMonth">This month</mat-button-toggle>
        <mat-button-toggle value="lastMonth">Last month</mat-button-toggle>
        <mat-button-toggle value="ytd">Year to date</mat-button-toggle>
        <mat-button-toggle value="last12Months">Last 12 months</mat-button-toggle>
        <mat-button-toggle value="custom">Custom</mat-button-toggle>
      </mat-button-toggle-group>

      <button mat-icon-button (click)="shift(-1)" aria-label="Previous range">
        <mat-icon>chevron_left</mat-icon>
      </button>
      <button mat-icon-button (click)="shift(1)" aria-label="Next range">
        <mat-icon>chevron_right</mat-icon>
      </button>

      @if (preset() === 'custom') {
        <mat-form-field appearance="outline">
          <mat-label>From</mat-label>
          <input matInput [matDatepicker]="fromPicker" [formControl]="fromCtrl" />
          <mat-datepicker-toggle matIconSuffix [for]="fromPicker"></mat-datepicker-toggle>
          <mat-datepicker #fromPicker></mat-datepicker>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>To</mat-label>
          <input matInput [matDatepicker]="toPicker" [formControl]="toCtrl" />
          <mat-datepicker-toggle matIconSuffix [for]="toPicker"></mat-datepicker-toggle>
          <mat-datepicker #toPicker></mat-datepicker>
        </mat-form-field>
      }

      <mat-form-field appearance="outline" class="owner-select">
        <mat-label>Owner</mat-label>
        <mat-select [formControl]="ownerIdCtrl">
          <mat-option [value]="null">All owners</mat-option>
          @for (o of owners(); track o.id) {
            <mat-option [value]="o.id">{{ o.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="accounts-select">
        <mat-label>Accounts</mat-label>
        <mat-select multiple [formControl]="accountIdsCtrl">
          @for (a of filteredAccounts(); track a.id) {
            <mat-option [value]="a.id">{{ a.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </div>

    <div class="summary">
      <mat-card>
        <mat-card-content>
          <div class="stat-label">Income</div>
          <div class="stat-value income">{{ totals().income | number:'1.2-2' }}</div>
        </mat-card-content>
      </mat-card>
      <mat-card>
        <mat-card-content>
          <div class="stat-label">Expenses</div>
          <div class="stat-value expense">{{ totals().expense | number:'1.2-2' }}</div>
        </mat-card-content>
      </mat-card>
      @if (hasTransfers()) {
        <mat-card>
          <mat-card-content>
            <div class="stat-label">Transfers (in / out)</div>
            <div class="stat-value transfers">
              <span class="income">{{ totals().transfersIn | number:'1.0-0' }}</span>
              <span class="sep"> / </span>
              <span class="expense">{{ totals().transfersOut | number:'1.0-0' }}</span>
            </div>
          </mat-card-content>
        </mat-card>
      }
      <mat-card>
        <mat-card-content>
          <div class="stat-label">Net</div>
          <div class="stat-value" [class.income]="totals().net >= 0" [class.expense]="totals().net < 0">
            {{ totals().net | number:'1.2-2' }}
          </div>
        </mat-card-content>
      </mat-card>
    </div>

    <div class="charts-grid" [class.charts-grid--single]="singleMonth()">
      <mat-card class="chart-card">
        <mat-card-header>
          <mat-card-title>Expense breakdown by category</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (pieData().length === 0) {
            <div class="empty">No expense data in this range.</div>
          } @else {
            <div class="breakdown-body" [class.breakdown-body--split]="singleMonth()">
            <div class="chart-wrap chart-wrap--pie">
              <ngx-charts-pie-chart
                [results]="pieData()"
                [scheme]="pieScheme"
                [labels]="true"
                [trimLabels]="false"
                [legend]="false"
                [tooltipText]="pieTooltip"
                (select)="onPieSelect($event)">
              </ngx-charts-pie-chart>
            </div>
            <table class="breakdown-table">
              <thead>
                <tr>
                  <th class="col-cat">Category</th>
                  <th class="col-num">Amount</th>
                  <th class="col-num">% of total</th>
                  <th class="col-num">Txns</th>
                </tr>
              </thead>
              <tbody>
                @for (row of breakdownRows(); track row.categoryId ?? row.name) {
                  <tr [class.clickable]="row.categoryId" (click)="onBreakdownRowClick(row)">
                    <td class="col-cat">{{ row.name }}</td>
                    <td class="col-num">{{ row.amount | number:'1.2-2' }}</td>
                    <td class="col-num">{{ row.pct | number:'1.1-1' }}%</td>
                    <td class="col-num">{{ row.count }}</td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr>
                  <td class="col-cat">Total</td>
                  <td class="col-num">{{ breakdownTotal() | number:'1.2-2' }}</td>
                  <td class="col-num">100.0%</td>
                  <td class="col-num">{{ breakdownTxnTotal() }}</td>
                </tr>
                @if (transfersOutItem(); as t) {
                  <tr class="transfers-row clickable" (click)="drilldownToTransfers()"
                    title="Show these transfers in the ledger">
                    <td class="col-cat">{{ t.categoryName }}</td>
                    <td class="col-num">{{ t.amount | number:'1.2-2' }}</td>
                    <td class="col-num">—</td>
                    <td class="col-num">{{ t.transactionCount }}</td>
                  </tr>
                }
              </tfoot>
            </table>
            </div>
          }
        </mat-card-content>
      </mat-card>

      @if (!singleMonth()) {
      <mat-card class="chart-card">
        <mat-card-header>
          <mat-card-title>Monthly cash flow</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (barData().length === 0) {
            <div class="empty">No transactions in this range.</div>
          } @else {
            <div class="chart-wrap chart-wrap--bar">
              <ngx-charts-bar-vertical-stacked
                [results]="barData()"
                [scheme]="cashflowScheme"
                [xAxis]="true"
                [yAxis]="true"
                [legend]="true"
                [legendPosition]="legendBelow"
                [showXAxisLabel]="false"
                [showYAxisLabel]="false"
                [yAxisTickFormatting]="currencyFormat">
              </ngx-charts-bar-vertical-stacked>
            </div>
            <div class="chart-wrap chart-wrap--line">
              <ngx-charts-line-chart
                [results]="lineData()"
                [scheme]="netLineScheme"
                [xAxis]="true"
                [yAxis]="true"
                [legend]="false"
                [autoScale]="true"
                [showXAxisLabel]="false"
                [showYAxisLabel]="true"
                yAxisLabel="Net"
                [yAxisTickFormatting]="currencyFormat">
              </ngx-charts-line-chart>
            </div>
          }
        </mat-card-content>
      </mat-card>
      }
    </div>

    <mat-card class="chart-card flow-card">
      <mat-card-header>
        <mat-card-title>Money flow</mat-card-title>
        <mat-card-subtitle>Income → accounts → transfers → expenses for the scoped accounts</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <app-sankey-chart [data]="sankey()"></app-sankey-chart>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    :host { display: block; padding-bottom: 32px; }
    .page-header { display: flex; align-items: baseline; justify-content: space-between; padding: 16px 0; gap: 12px; }
    .muted { color: rgba(0,0,0,0.55); font-size: 0.95rem; }
    .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding-bottom: 16px; }
    .filters mat-form-field { min-width: 180px; }
    .owner-select { min-width: 160px; margin-left: auto; }
    .accounts-select { min-width: 220px; }
    .summary { display: flex; flex-wrap: wrap; gap: 12px; padding-bottom: 16px; }
    .summary mat-card { flex: 1 1 180px; min-width: 180px; }
    .stat-label { font-size: 0.85rem; color: rgba(0,0,0,0.55); margin-bottom: 4px; }
    .stat-value { font-size: 1.6rem; font-weight: 500; }
    .stat-value.income { color: #2e7d32; }
    .stat-value.expense { color: #b00020; }
    .stat-value.transfers { font-size: 1.3rem; }
    .stat-value.transfers .sep { color: rgba(0,0,0,0.35); font-weight: 400; }
    .flow-card { margin-top: 16px; }
    .breakdown-table tfoot tr.transfers-row td { font-weight: 500; color: #ef6c00; border-top: 1px dashed rgba(0,0,0,0.18); }
    .charts-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
    @media (min-width: 1280px) { .charts-grid:not(.charts-grid--single) { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); } }
    .chart-card { min-width: 0; }
    .chart-card mat-card-content { display: flex; flex-direction: column; gap: 8px; min-width: 0; }
    .breakdown-body { display: flex; flex-direction: column; gap: 8px; min-width: 0; }
    @media (min-width: 900px) {
      .breakdown-body--split { display: grid; grid-template-columns: minmax(280px, 1fr) minmax(0, 1.4fr); gap: 24px; align-items: start; }
    }
    .chart-wrap { width: 100%; min-width: 0; }
    .chart-wrap--pie { height: 320px; max-width: 460px; margin-left: auto; margin-right: auto; }
    .chart-wrap--bar { height: 320px; }
    .chart-wrap--line { height: 200px; }
    .chart-wrap :where(ngx-charts-pie-chart, ngx-charts-bar-vertical-stacked, ngx-charts-line-chart) { display: block; width: 100%; height: 100%; }
    .empty { padding: 32px; text-align: center; color: rgba(0,0,0,0.55); }
    .breakdown-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; margin-top: 8px; }
    .breakdown-table th, .breakdown-table td { padding: 6px 8px; border-bottom: 1px solid rgba(0,0,0,0.08); }
    .breakdown-table thead th { text-align: left; font-weight: 600; color: rgba(0,0,0,0.7); border-bottom: 1px solid rgba(0,0,0,0.18); }
    .breakdown-table .col-num { text-align: right; font-variant-numeric: tabular-nums; }
    .breakdown-table tfoot td { font-weight: 600; border-top: 1px solid rgba(0,0,0,0.18); border-bottom: none; }
    .breakdown-table tr.clickable { cursor: pointer; }
    .breakdown-table tr.clickable:hover { background: rgba(0,0,0,0.04); }
  `],
})
export class AnalyticsPage {
  private svc = inject(AnalyticsService);
  private accountsSvc = inject(AccountsService);
  private ownersSvc = inject(OwnersService);
  private snack = inject(MatSnackBar);
  private familyCtx = inject(FamilyContextService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  readonly legendBelow = LegendPosition.Below;
  readonly pieScheme: Color = {
    name: 'pie',
    selectable: true,
    group: ScaleType.Ordinal,
    domain: ['#1976d2', '#7b1fa2', '#388e3c', '#f57c00', '#0288d1', '#c2185b', '#5d4037', '#455a64', '#00796b', '#fbc02d'],
  };
  readonly cashflowScheme: Color = {
    name: 'cashflow',
    selectable: true,
    group: ScaleType.Ordinal,
    // Income, Expense, Transfers in, Transfers out — order matches the series order in barData().
    domain: ['#2e7d32', '#b00020', '#66bb6a', '#ef6c00'],
  };
  readonly netLineScheme: Color = {
    name: 'net',
    selectable: true,
    group: ScaleType.Ordinal,
    domain: ['#1976d2'],
  };
  readonly currencyFormat = (v: number) =>
    Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(v);
  readonly pieTooltip = ({ data }: { data: { name: string; value: number } }) =>
    `${data.name}: ${this.currencyFormat(data.value)}`;

  fromCtrl = new FormControl<Date | null>(null);
  toCtrl = new FormControl<Date | null>(null);
  ownerIdCtrl = new FormControl<string | null>(null);
  accountIdsCtrl = new FormControl<string[]>([], { nonNullable: true });

  preset = signal<RangePreset>('thisMonth');
  range = signal<DateRange>(presetRange('thisMonth'));
  ownerId = signal<string | null>(null);
  accountIds = signal<string[]>([]);
  private hydrated = false;

  owners = signal<OwnerDto[]>([]);
  accounts = signal<AccountDto[]>([]);

  filteredAccounts = computed<AccountDto[]>(() => {
    const oid = this.ownerId();
    const all = this.accounts();
    return oid ? all.filter(a => a.ownerId === oid) : all;
  });

  // The accounts actually analyzed: an explicit account selection wins; otherwise
  // a selected owner means all of that owner's accounts; otherwise all family accounts.
  effectiveAccountIds = computed<string[]>(() => {
    const selected = this.accountIds();
    if (selected.length) return selected;
    const oid = this.ownerId();
    if (oid) return this.accounts().filter(a => a.ownerId === oid).map(a => a.id);
    return [];
  });
  pie = signal<CategoryBreakdownItem[]>([]);
  cashflow = signal<MonthlyCashflowItem[]>([]);
  sankey = signal<SankeyData | null>(null);

  // The synthetic "Transfers out" row is split out from the real spending categories so it
  // never appears in the pie or the category total, only as a distinct footer line.
  realBreakdown = computed<CategoryBreakdownItem[]>(() => this.pie().filter(b => !b.isTransfersBucket));
  transfersOutItem = computed<CategoryBreakdownItem | undefined>(() => this.pie().find(b => b.isTransfersBucket));

  rangeLabel = computed(() => formatRangeLabel(this.preset(), this.range()));

  singleMonth = computed(() => {
    const { from, to } = this.range();
    return from.getFullYear() === to.getFullYear() && from.getMonth() === to.getMonth();
  });

  pieData = computed<PieDatum[]>(() =>
    this.realBreakdown().map(b => ({ name: b.categoryName, value: b.amount })));

  breakdownRows = computed<BreakdownRow[]>(() => {
    const items = this.realBreakdown();
    const total = items.reduce((s, b) => s + b.amount, 0);
    return [...items]
      .sort((a, b) => b.amount - a.amount)
      .map(b => ({
        categoryId: b.categoryId,
        name: b.categoryName,
        amount: b.amount,
        count: b.transactionCount,
        pct: total > 0 ? (b.amount / total) * 100 : 0,
      }));
  });

  breakdownTotal = computed(() => this.realBreakdown().reduce((s, b) => s + b.amount, 0));
  breakdownTxnTotal = computed(() => this.realBreakdown().reduce((s, b) => s + b.transactionCount, 0));

  hasTransfers = computed(() => {
    const t = this.totals();
    return t.transfersIn !== 0 || t.transfersOut !== 0;
  });

  barData = computed<BarGroup[]>(() => {
    const showTransfers = this.hasTransfers();
    return this.cashflow().map(m => ({
      name: this.monthLabel(m),
      series: [
        { name: 'Income', value: m.income },
        { name: 'Expense', value: m.expense },
        ...(showTransfers
          ? [
              { name: 'Transfers in', value: m.transfersIn },
              { name: 'Transfers out', value: m.transfersOut },
            ]
          : []),
      ],
    }));
  });

  lineData = computed<LineSeries[]>(() => [{
    name: 'Net',
    series: this.cashflow().map(m => ({
      name: this.monthLabel(m),
      value: m.net,
    })),
  }]);

  totals = computed(() => {
    const cf = this.cashflow();
    const income = cf.reduce((s, m) => s + m.income, 0);
    const expense = cf.reduce((s, m) => s + m.expense, 0);
    const transfersIn = cf.reduce((s, m) => s + m.transfersIn, 0);
    const transfersOut = cf.reduce((s, m) => s + m.transfersOut, 0);
    return { income, expense, transfersIn, transfersOut, net: income + expense + transfersIn + transfersOut };
  });

  constructor() {
    this.hydrateFromUrl();

    this.accountIdsCtrl.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(v => this.accountIds.set(v ?? []));

    this.ownerIdCtrl.valueChanges.pipe(takeUntilDestroyed()).subscribe(oid => {
      this.ownerId.set(oid);
      // Drop any selected accounts that the newly chosen owner doesn't own.
      if (oid) {
        const allowed = new Set(this.accounts().filter(a => a.ownerId === oid).map(a => a.id));
        const pruned = this.accountIds().filter(id => allowed.has(id));
        if (pruned.length !== this.accountIds().length) {
          this.accountIdsCtrl.setValue(pruned);
        }
      }
    });

    this.fromCtrl.valueChanges.pipe(takeUntilDestroyed()).subscribe(d => {
      if (this.preset() !== 'custom' || !d) return;
      this.range.update(r => ({ ...r, from: d }));
    });
    this.toCtrl.valueChanges.pipe(takeUntilDestroyed()).subscribe(d => {
      if (this.preset() !== 'custom' || !d) return;
      this.range.update(r => ({ ...r, to: d }));
    });

    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      this.accountsSvc.list().subscribe({
        next: a => this.accounts.set(a),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
      this.ownersSvc.list().subscribe({
        next: o => this.owners.set(o),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
    });

    effect(() => {
      const preset = this.preset();
      const range = this.range();
      const ownerId = this.ownerId();
      const accountIds = this.accountIds();
      if (!this.hydrated) return;
      this.syncUrl(preset, range, ownerId, accountIds);
    });

    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      const range = this.range();
      const accountIds = this.effectiveAccountIds();
      const query = { from: range.from, to: range.to, accountIds };
      this.svc.categoryBreakdown(query).subscribe({
        next: r => this.pie.set(r),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
      this.svc.monthlyCashflow(query).subscribe({
        next: r => this.cashflow.set(r),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
      this.svc.sankey(query).subscribe({
        next: r => this.sankey.set(r),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
    });
  }

  onPresetChange(p: RangePreset) {
    this.preset.set(p);
    if (p !== 'custom') {
      const r = presetRange(p);
      this.range.set(r);
      this.fromCtrl.setValue(r.from, { emitEvent: false });
      this.toCtrl.setValue(r.to, { emitEvent: false });
    }
  }

  onPieSelect(event: { name: string; value: number }) {
    const item = this.pie().find(p => p.categoryName === event.name);
    if (!item) return;
    this.drilldownToCategory(item.categoryId);
  }

  onBreakdownRowClick(row: BreakdownRow) {
    this.drilldownToCategory(row.categoryId);
  }

  // Deep-link the "Transfers out" line to the ledger, filtered to the same scope + range with
  // the transfers-only filter on, so the user can see exactly which transactions it covers.
  drilldownToTransfers() {
    const range = this.range();
    const accountIds = this.effectiveAccountIds();
    this.router.navigate(['/ledger'], {
      queryParams: {
        isTransfer: 'true',
        from: dateToYmd(range.from),
        to: dateToYmd(range.to),
        accountIds: accountIds.length ? accountIds.join(',') : null,
      },
    });
  }

  private drilldownToCategory(categoryId: string | null) {
    if (!categoryId) {
      this.snack.open('Drill-down for Uncategorized is not supported yet.', 'Close', { duration: 3000 });
      return;
    }
    const range = this.range();
    const accountIds = this.effectiveAccountIds();
    this.router.navigate(['/ledger'], {
      queryParams: {
        categoryIds: categoryId,
        from: dateToYmd(range.from),
        to: dateToYmd(range.to),
        accountIds: accountIds.length ? accountIds.join(',') : null,
      },
    });
  }

  shift(direction: -1 | 1) {
    const r = shiftRange(this.preset(), this.range(), direction);
    this.range.set(r);
    if (this.preset() === 'custom') {
      this.fromCtrl.setValue(r.from, { emitEvent: false });
      this.toCtrl.setValue(r.to, { emitEvent: false });
    }
  }

  private monthLabel(m: MonthlyCashflowItem): string {
    const d = new Date(m.year, m.month - 1, 1);
    return Intl.DateTimeFormat(undefined, { month: 'short', year: '2-digit' }).format(d);
  }

  private hydrateFromUrl() {
    const params = this.route.snapshot.queryParamMap;
    const rawPreset = params.get('preset') as RangePreset | null;
    const validPresets: RangePreset[] = ['thisMonth', 'lastMonth', 'ytd', 'last12Months', 'custom'];
    const preset: RangePreset = rawPreset && validPresets.includes(rawPreset) ? rawPreset : 'thisMonth';

    const from = parseYmd(params.get('from'));
    const to = parseYmd(params.get('to'));
    const range: DateRange = (from && to) ? { from, to } : presetRange(preset);

    const accountIds = (params.get('accountIds') ?? '')
      .split(',').map(s => s.trim()).filter(s => s.length > 0);

    const ownerId = params.get('owner') || null;

    this.preset.set(preset);
    this.range.set(range);
    this.ownerId.set(ownerId);
    this.ownerIdCtrl.setValue(ownerId, { emitEvent: false });
    this.accountIds.set(accountIds);
    this.accountIdsCtrl.setValue(accountIds, { emitEvent: false });
    if (preset === 'custom') {
      this.fromCtrl.setValue(range.from, { emitEvent: false });
      this.toCtrl.setValue(range.to, { emitEvent: false });
    }
    this.hydrated = true;
  }

  private syncUrl(preset: RangePreset, range: DateRange, ownerId: string | null, accountIds: string[]) {
    this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        preset,
        from: dateToYmd(range.from),
        to: dateToYmd(range.to),
        owner: ownerId,
        accountIds: accountIds.length ? accountIds.join(',') : null,
      },
      queryParamsHandling: 'merge',
    });
  }
}

function parseYmd(s: string | null): Date | null {
  if (!s) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  if (!m) return null;
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return isNaN(d.getTime()) ? null : d;
}

function dateToYmd(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}
