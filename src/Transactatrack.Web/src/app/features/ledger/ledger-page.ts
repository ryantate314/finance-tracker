import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Subject, merge } from 'rxjs';
import { debounceTime, switchMap } from 'rxjs/operators';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamilyContextService } from '../../core/family-context/family-context.service';
import { AccountDto, AccountsService } from '../accounts/accounts.service';
import { CategoryPicker, CategorySelection } from '../categories/category-picker.component';
import { CategoriesService, CategoryDto } from '../categories/categories.service';
import { CategoryRulesService, SaveCategoryRuleRequest } from '../rules/category-rules.service';
import { RuleEditDialog } from '../rules/rule-edit-dialog';
import { TransactionsService } from '../transactions/transactions.service';
import { LedgerQuery, LedgerService, PagedResult, TransactionDto } from './ledger.service';

@Component({
  selector: 'app-ledger-page',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule,
    MatTableModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatDatepickerModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatCheckboxModule,
    CategoryPicker,
    DatePipe,
    DecimalPipe,
  ],
  template: `
    <div class="page-header">
      <h2>Ledger</h2>
      <span class="muted">{{ result()?.totalCount ?? 0 }} rows</span>
    </div>

    <div class="filters">
      <mat-form-field appearance="outline">
        <mat-label>Search</mat-label>
        <input matInput [formControl]="qCtrl" placeholder="Description / merchant" />
      </mat-form-field>

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

      <mat-form-field appearance="outline">
        <mat-label>Accounts</mat-label>
        <mat-select multiple [formControl]="accountIdsCtrl">
          @for (a of accounts(); track a.id) {
            <mat-option [value]="a.id">{{ a.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Categories</mat-label>
        <mat-select multiple [formControl]="categoryIdsCtrl">
          @for (c of categories(); track c.id) {
            <mat-option [value]="c.id">{{ c.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-checkbox [formControl]="needsReviewCtrl" class="review-check">Needs review</mat-checkbox>

      <button mat-stroked-button (click)="clearFilters()">Clear</button>
    </div>

    <mat-table [dataSource]="result()?.items ?? []">
      <ng-container matColumnDef="date">
        <mat-header-cell *matHeaderCellDef>Date</mat-header-cell>
        <mat-cell *matCellDef="let t">{{ t.date | date:'shortDate' }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="description">
        <mat-header-cell *matHeaderCellDef>Description</mat-header-cell>
        <mat-cell *matCellDef="let t">{{ t.description }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="account">
        <mat-header-cell *matHeaderCellDef>Account</mat-header-cell>
        <mat-cell *matCellDef="let t">{{ accountName(t.accountId) }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="category">
        <mat-header-cell *matHeaderCellDef>Category</mat-header-cell>
        <mat-cell *matCellDef="let t">
          <div class="cat-cell">
            <app-category-picker
              [categories]="categories()"
              [categoryId]="t.categoryId"
              [subCategoryId]="t.subCategoryId"
              (selectionChange)="onCategorySelection(t, $event)">
            </app-category-picker>
            @if (t.needsReview && t.categorizationSource === 'Llm') {
              <span class="source-chip ai">AI</span>
            } @else if (t.categorizationSource === 'Rule' && t.appliedRuleId) {
              <button type="button" class="source-chip rule"
                (click)="openAppliedRule(t.appliedRuleId)"
                title="Edit the rule that categorized this transaction">Rule</button>
            }
          </div>
        </mat-cell>
      </ng-container>
      <ng-container matColumnDef="amount">
        <mat-header-cell *matHeaderCellDef class="right">Amount</mat-header-cell>
        <mat-cell *matCellDef="let t" class="right" [class.debit]="t.amount < 0">
          {{ t.amount | number:'1.2-2' }}
        </mat-cell>
      </ng-container>
      <ng-container matColumnDef="actions">
        <mat-header-cell *matHeaderCellDef class="actions-cell"></mat-header-cell>
        <mat-cell *matCellDef="let t" class="actions-cell">
          <button mat-icon-button [matMenuTriggerFor]="rowMenu"
            [matMenuTriggerData]="{ row: t }"
            aria-label="Row actions">
            <mat-icon>more_vert</mat-icon>
          </button>
        </mat-cell>
      </ng-container>
      <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
      <mat-row *matRowDef="let row; columns: columns"></mat-row>
    </mat-table>

    <mat-menu #rowMenu="matMenu">
      <ng-template matMenuContent let-row="row">
        <button mat-menu-item (click)="createRuleFrom(row)">
          <mat-icon>rule</mat-icon>
          <span>Create rule from this transaction</span>
        </button>
      </ng-template>
    </mat-menu>

    <mat-paginator
      [length]="result()?.totalCount ?? 0"
      [pageSize]="pageSize()"
      [pageIndex]="page() - 1"
      [pageSizeOptions]="[25, 50, 100, 200]"
      (page)="onPageChange($event)">
    </mat-paginator>
  `,
  styles: [`
    .page-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 0; }
    .muted { color: rgba(0,0,0,0.55); font-size: 0.875rem; }
    .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start; padding-bottom: 8px; }
    .filters mat-form-field { min-width: 180px; }
    .review-check { padding-top: 8px; }
    .right { justify-content: flex-end; text-align: right; }
    .debit { color: #b00020; }
    .cat-cell { display: flex; align-items: center; gap: 6px; }
    .mat-column-date { flex: 0 0 100px; }
    .mat-column-description { flex: 3 1 240px; }
    .mat-column-account { flex: 0 0 140px; }
    .mat-column-category { overflow: visible; flex: 1 1 280px; }
    .mat-column-amount { flex: 0 0 110px; }
    .source-chip { font-size: 0.7rem; padding: 2px 8px; border-radius: 10px; font-weight: 600; white-space: nowrap; border: 0; }
    .source-chip.ai { background: #e3f2fd; color: #1565c0; }
    .source-chip.rule { background: #e8f5e9; color: #2e7d32; }
    button.source-chip { cursor: pointer; }
    button.source-chip:hover { filter: brightness(0.95); }
    .actions-cell { flex: 0 0 56px; justify-content: center; padding: 0; }
  `],
})
export class LedgerPage {
  private svc = inject(LedgerService);
  private txSvc = inject(TransactionsService);
  private accountsSvc = inject(AccountsService);
  private categoriesSvc = inject(CategoriesService);
  private rulesSvc = inject(CategoryRulesService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private familyCtx = inject(FamilyContextService);

  qCtrl = new FormControl('', { nonNullable: true });
  fromCtrl = new FormControl<Date | null>(null);
  toCtrl = new FormControl<Date | null>(null);
  accountIdsCtrl = new FormControl<string[]>([], { nonNullable: true });
  categoryIdsCtrl = new FormControl<string[]>([], { nonNullable: true });
  needsReviewCtrl = new FormControl(false, { nonNullable: true });

  page = signal(1);
  pageSize = signal(50);
  result = signal<PagedResult<TransactionDto> | null>(null);

  accounts = signal<AccountDto[]>([]);
  categories = signal<CategoryDto[]>([]);
  columns = ['date', 'description', 'account', 'category', 'amount', 'actions'];

  private accountsById = computed(() =>
    new Map(this.accounts().map(a => [a.id, a.name])));

  private q = toSignal(this.qCtrl.valueChanges.pipe(debounceTime(250)), { initialValue: '' });
  private from = toSignal(this.fromCtrl.valueChanges, { initialValue: null });
  private to = toSignal(this.toCtrl.valueChanges, { initialValue: null });
  private accountIds = toSignal(this.accountIdsCtrl.valueChanges, { initialValue: [] as string[] });
  private categoryIds = toSignal(this.categoryIdsCtrl.valueChanges, { initialValue: [] as string[] });
  private needsReview = toSignal(this.needsReviewCtrl.valueChanges, { initialValue: false });

  private query$ = new Subject<LedgerQuery>();

  constructor() {
    merge(
      this.qCtrl.valueChanges.pipe(debounceTime(250)),
      this.fromCtrl.valueChanges,
      this.toCtrl.valueChanges,
      this.accountIdsCtrl.valueChanges,
      this.categoryIdsCtrl.valueChanges,
      this.needsReviewCtrl.valueChanges,
    ).pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.page() !== 1) this.page.set(1);
    });

    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      this.accountsSvc.list().subscribe({
        next: a => this.accounts.set(a),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
      this.categoriesSvc.list().subscribe({
        next: c => this.categories.set(c),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
    });

    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      const nr = this.needsReview();
      this.query$.next({
        accountIds: this.accountIds(),
        categoryIds: this.categoryIds(),
        from: this.from(),
        to: this.to(),
        q: this.q(),
        needsReview: nr || undefined,
        page: this.page(),
        pageSize: this.pageSize(),
      });
    });

    this.query$.pipe(
      switchMap(q => this.svc.list(q)),
      takeUntilDestroyed(),
    ).subscribe({
      next: r => this.result.set(r),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  accountName(id: string): string { return this.accountsById().get(id) ?? id; }

  onCategorySelection(tx: TransactionDto, selection: CategorySelection) {
    this.txSvc.updateCategory(tx.id, selection.categoryId, selection.subCategoryId).subscribe({
      next: updated => this.result.update(r => r
        ? { ...r, items: r.items.map(t => t.id === updated.id ? updated : t) }
        : r),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  openAppliedRule(ruleId: string) {
    this.rulesSvc.get(ruleId).subscribe({
      next: rule => {
        this.dialog.open(RuleEditDialog, { data: { rule }, width: '480px' })
          .afterClosed().subscribe((req: SaveCategoryRuleRequest | undefined) => {
            if (!req) return;
            this.rulesSvc.update(rule.id, req).subscribe({
              next: () => this.snack.open('Rule updated', 'Close', { duration: 3000 }),
              error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
            });
          });
      },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  createRuleFrom(tx: TransactionDto) {
    const prefill: Partial<SaveCategoryRuleRequest> = {
      matchField: 'Description',
      matchType: 'Contains',
      pattern: tx.description,
      targetCategoryId: tx.categoryId ?? '',
      targetSubCategoryId: tx.subCategoryId ?? null,
      scope: 'Family',
      priority: 10,
      isEnabled: true,
    };
    this.dialog.open(RuleEditDialog, { data: { prefill }, width: '480px' })
      .afterClosed().subscribe((req: SaveCategoryRuleRequest | undefined) => {
        if (!req) return;
        this.rulesSvc.create(req).subscribe({
          next: () => this.snack.open('Rule created', 'Close', { duration: 3000 }),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  onPageChange(e: PageEvent) {
    this.pageSize.set(e.pageSize);
    this.page.set(e.pageIndex + 1);
  }

  clearFilters() {
    this.qCtrl.setValue('');
    this.fromCtrl.setValue(null);
    this.toCtrl.setValue(null);
    this.accountIdsCtrl.setValue([]);
    this.categoryIdsCtrl.setValue([]);
    this.needsReviewCtrl.setValue(false);
    this.page.set(1);
  }
}
