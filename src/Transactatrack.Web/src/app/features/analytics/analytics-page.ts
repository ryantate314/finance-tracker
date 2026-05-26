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
import { AnalyticsService, CategoryBreakdownItem, MonthlyCashflowItem } from './analytics.service';
import { DateRange, RangePreset, formatRangeLabel, presetRange, shiftRange } from './time-range';

interface PieDatum { name: string; value: number; }
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

      <mat-form-field appearance="outline" class="accounts-select">
        <mat-label>Accounts</mat-label>
        <mat-select multiple [formControl]="accountIdsCtrl">
          @for (a of accounts(); track a.id) {
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
      <mat-card>
        <mat-card-content>
          <div class="stat-label">Net</div>
          <div class="stat-value" [class.income]="totals().net >= 0" [class.expense]="totals().net < 0">
            {{ totals().net | number:'1.2-2' }}
          </div>
        </mat-card-content>
      </mat-card>
    </div>

    <div class="charts-grid">
      <mat-card class="chart-card">
        <mat-card-header>
          <mat-card-title>Expense breakdown by category</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (pieData().length === 0) {
            <div class="empty">No expense data in this range.</div>
          } @else {
            <ngx-charts-pie-chart
              [view]="[700, 420]"
              [results]="pieData()"
              [scheme]="pieScheme"
              [labels]="true"
              [trimLabels]="false"
              [legend]="true"
              [legendPosition]="legendBelow"
              [tooltipText]="pieTooltip"
              (select)="onPieSelect($event)">
            </ngx-charts-pie-chart>
          }
        </mat-card-content>
      </mat-card>

      <mat-card class="chart-card">
        <mat-card-header>
          <mat-card-title>Monthly cash flow</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (barData().length === 0) {
            <div class="empty">No transactions in this range.</div>
          } @else {
            <ngx-charts-bar-vertical-stacked
              [view]="[700, 320]"
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
            <ngx-charts-line-chart
              [view]="[700, 180]"
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
          }
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .page-header { display: flex; align-items: baseline; justify-content: space-between; padding: 16px 0; gap: 12px; }
    .muted { color: rgba(0,0,0,0.55); font-size: 0.95rem; }
    .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding-bottom: 16px; }
    .filters mat-form-field { min-width: 180px; }
    .accounts-select { min-width: 220px; margin-left: auto; }
    .summary { display: flex; flex-wrap: wrap; gap: 12px; padding-bottom: 16px; }
    .summary mat-card { flex: 1 1 180px; min-width: 180px; }
    .stat-label { font-size: 0.85rem; color: rgba(0,0,0,0.55); margin-bottom: 4px; }
    .stat-value { font-size: 1.6rem; font-weight: 500; }
    .stat-value.income { color: #2e7d32; }
    .stat-value.expense { color: #b00020; }
    .charts-grid { display: grid; grid-template-columns: 1fr; gap: 16px; }
    @media (min-width: 1280px) { .charts-grid { grid-template-columns: 1fr 1fr; } }
    .chart-card mat-card-content { display: flex; flex-direction: column; gap: 8px; }
    .empty { padding: 32px; text-align: center; color: rgba(0,0,0,0.55); }
  `],
})
export class AnalyticsPage {
  private svc = inject(AnalyticsService);
  private accountsSvc = inject(AccountsService);
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
    domain: ['#2e7d32', '#b00020'],
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
  accountIdsCtrl = new FormControl<string[]>([], { nonNullable: true });

  preset = signal<RangePreset>('thisMonth');
  range = signal<DateRange>(presetRange('thisMonth'));
  accountIds = signal<string[]>([]);
  private hydrated = false;

  accounts = signal<AccountDto[]>([]);
  pie = signal<CategoryBreakdownItem[]>([]);
  cashflow = signal<MonthlyCashflowItem[]>([]);

  rangeLabel = computed(() => formatRangeLabel(this.preset(), this.range()));

  pieData = computed<PieDatum[]>(() =>
    this.pie().map(b => ({ name: b.categoryName, value: b.amount })));

  barData = computed<BarGroup[]>(() =>
    this.cashflow().map(m => ({
      name: this.monthLabel(m),
      series: [
        { name: 'Income', value: m.income },
        { name: 'Expense', value: m.expense },
      ],
    })));

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
    return { income, expense, net: income + expense };
  });

  constructor() {
    this.hydrateFromUrl();

    this.accountIdsCtrl.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(v => this.accountIds.set(v ?? []));

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
    });

    effect(() => {
      const preset = this.preset();
      const range = this.range();
      const accountIds = this.accountIds();
      if (!this.hydrated) return;
      this.syncUrl(preset, range, accountIds);
    });

    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      const range = this.range();
      const accountIds = this.accountIds();
      const query = { from: range.from, to: range.to, accountIds };
      this.svc.categoryBreakdown(query).subscribe({
        next: r => this.pie.set(r),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
      this.svc.monthlyCashflow(query).subscribe({
        next: r => this.cashflow.set(r),
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
    if (!item.categoryId) {
      this.snack.open('Drill-down for Uncategorized is not supported yet.', 'Close', { duration: 3000 });
      return;
    }
    const range = this.range();
    this.router.navigate(['/ledger'], {
      queryParams: {
        categoryIds: item.categoryId,
        from: dateToYmd(range.from),
        to: dateToYmd(range.to),
        accountIds: this.accountIds().length ? this.accountIds().join(',') : null,
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

    this.preset.set(preset);
    this.range.set(range);
    this.accountIds.set(accountIds);
    this.accountIdsCtrl.setValue(accountIds, { emitEvent: false });
    if (preset === 'custom') {
      this.fromCtrl.setValue(range.from, { emitEvent: false });
      this.toCtrl.setValue(range.to, { emitEvent: false });
    }
    this.hydrated = true;
  }

  private syncUrl(preset: RangePreset, range: DateRange, accountIds: string[]) {
    this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        preset,
        from: dateToYmd(range.from),
        to: dateToYmd(range.to),
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
