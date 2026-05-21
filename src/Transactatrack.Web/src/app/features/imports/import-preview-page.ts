import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs/operators';
import { extractErrorMessage } from '../../core/api/api-error';
import { CategoryPicker, CategorySelection } from '../categories/category-picker.component';
import { CategoriesService, CategoryDto } from '../categories/categories.service';
import { CategoryRulesService, SaveCategoryRuleRequest } from '../rules/category-rules.service';
import { RuleEditDialog } from '../rules/rule-edit-dialog';
import { TransactionsService } from '../transactions/transactions.service';
import { ImportBatchDetailDto, ImportBatchDto, ImportPreviewDto, ImportPreviewRowDto, ImportsService } from './imports.service';

@Component({
  selector: 'app-import-preview-page',
  standalone: true,
  imports: [
    MatTableModule, MatButtonModule, MatIconModule, MatMenuModule,
    MatChipsModule, MatProgressBarModule, CategoryPicker, DatePipe, DecimalPipe,
  ],
  template: `
    <div class="page-header">
      <h2>Import Preview</h2>
    </div>
    @if (detail(); as d) {
      <div class="meta">
        <div><strong>File:</strong> {{ d.batch.originalFilename }}</div>
        <div><strong>Bank:</strong> {{ d.batch.bankCode }}</div>
        <div><strong>Status:</strong> {{ d.batch.status }}</div>
        <div><strong>Rows:</strong> {{ d.batch.transactionCount }}</div>
        @if (uploadPreview(); as up) {
          <div><strong>New:</strong> {{ up.newCount }}</div>
          <div><strong>Duplicates:</strong> {{ up.duplicateCount }}</div>
        }
        <div><strong>Uploaded:</strong> {{ d.batch.uploadedUtc | date:'medium' }}</div>
      </div>

      @if (d.batch.status === 'Pending') {
        <div class="batch-actions">
          <button mat-stroked-button (click)="rerunRules()" [disabled]="d.batch.llmStatus === 'Running'">
            <mat-icon>rule</mat-icon> Re-run Rules
          </button>
          <button mat-stroked-button (click)="suggestLlm()"
            [disabled]="d.batch.llmStatus === 'Running' || uncategorizedCount() === 0">
            <mat-icon>psychology</mat-icon>
            Suggest with AI ({{ uncategorizedCount() }} uncategorized)
          </button>
        </div>

        @if (d.batch.llmStatus === 'Running') {
          <div class="llm-progress">
            <span>AI categorization in progress…</span>
            <mat-progress-bar mode="determinate"
              [value]="d.batch.llmRowsTotal > 0 ? (d.batch.llmRowsDone / d.batch.llmRowsTotal * 100) : 0">
            </mat-progress-bar>
            <span class="muted">{{ d.batch.llmRowsDone }} / {{ d.batch.llmRowsTotal }} rows</span>
          </div>
        }
        @if (d.batch.llmStatus === 'Failed') {
          <p class="error-msg">AI categorization failed. Try again or categorize manually.</p>
        }
      }

      @if (newRows().length > 0) {
        <h3>New rows (will be imported)</h3>
        <mat-table [dataSource]="newRows()">
          <ng-container matColumnDef="date">
            <mat-header-cell *matHeaderCellDef>Date</mat-header-cell>
            <mat-cell *matCellDef="let t">{{ t.date | date:'shortDate' }}</mat-cell>
          </ng-container>
          <ng-container matColumnDef="description">
            <mat-header-cell *matHeaderCellDef>Description</mat-header-cell>
            <mat-cell *matCellDef="let t">{{ t.description }}</mat-cell>
          </ng-container>
          <ng-container matColumnDef="amount">
            <mat-header-cell *matHeaderCellDef class="right">Amount</mat-header-cell>
            <mat-cell *matCellDef="let t" class="right" [class.debit]="t.amount < 0">
              {{ t.amount | number:'1.2-2' }}
            </mat-cell>
          </ng-container>
          <ng-container matColumnDef="category">
            <mat-header-cell *matHeaderCellDef>Category</mat-header-cell>
            <mat-cell *matCellDef="let t">
              <div class="cat-cell">
                <app-category-picker
                  [categories]="categories()"
                  [categoryId]="t.categoryId"
                  [subCategoryId]="t.subCategoryId"
                  [disabled]="detail()?.batch?.status !== 'Pending'"
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
      }

      @if (duplicateRows().length > 0) {
        <h3 class="dup-heading">Duplicate rows (skipped)</h3>
        <p class="muted">
          These rows already match a transaction in this account and were not imported.
        </p>
        <mat-table [dataSource]="duplicateRows()" class="dup-table">
          <ng-container matColumnDef="date">
            <mat-header-cell *matHeaderCellDef>Date</mat-header-cell>
            <mat-cell *matCellDef="let t">{{ t.date | date:'shortDate' }}</mat-cell>
          </ng-container>
          <ng-container matColumnDef="description">
            <mat-header-cell *matHeaderCellDef>Description</mat-header-cell>
            <mat-cell *matCellDef="let t">{{ t.description }}</mat-cell>
          </ng-container>
          <ng-container matColumnDef="amount">
            <mat-header-cell *matHeaderCellDef class="right">Amount</mat-header-cell>
            <mat-cell *matCellDef="let t" class="right" [class.debit]="t.amount < 0">
              {{ t.amount | number:'1.2-2' }}
            </mat-cell>
          </ng-container>
          <mat-header-row *matHeaderRowDef="dupColumns"></mat-header-row>
          <mat-row *matRowDef="let row; columns: dupColumns"></mat-row>
        </mat-table>
      }

      @if (d.batch.status === 'Pending') {
        <div class="actions">
          <button mat-stroked-button color="warn" (click)="discard()">Discard</button>
          <button mat-flat-button (click)="commit()">Commit</button>
        </div>
      }
    } @else if (loading()) {
      <p>Loading…</p>
    }
  `,
  styles: [`
    .page-header { display: flex; align-items: center; padding: 16px 0; }
    .meta { display: flex; gap: 24px; flex-wrap: wrap; padding: 8px 0 16px; }
    .batch-actions { display: flex; gap: 8px; padding: 8px 0; }
    .llm-progress { display: flex; flex-direction: column; gap: 4px; padding: 8px 0 16px; max-width: 480px; }
    .actions { display: flex; gap: 8px; justify-content: flex-end; padding: 16px 0; }
    .right { justify-content: flex-end; text-align: right; }
    .debit { color: #b00020; }
    .dup-heading { margin-top: 24px; color: #856404; }
    .dup-table mat-row { background: #fff8e6; }
    .muted { color: rgba(0,0,0,0.55); margin: 0 0 8px; }
    .error-msg { color: #b00020; }
    .cat-cell { display: flex; align-items: center; gap: 6px; }
    .mat-column-date { flex: 0 0 100px; }
    .mat-column-description { flex: 3 1 240px; }
    .mat-column-amount { flex: 0 0 110px; }
    .mat-column-category { overflow: visible; flex: 1 1 280px; }
    .actions-cell { flex: 0 0 56px; justify-content: center; padding: 0; }
    .source-chip { font-size: 0.7rem; padding: 2px 8px; border-radius: 10px; font-weight: 600; white-space: nowrap; border: 0; }
    .source-chip.ai { background: #e3f2fd; color: #1565c0; }
    .source-chip.rule { background: #e8f5e9; color: #2e7d32; }
    button.source-chip { cursor: pointer; }
    button.source-chip:hover { filter: brightness(0.95); }
  `],
})
export class ImportPreviewPage {
  private svc = inject(ImportsService);
  private categoriesSvc = inject(CategoriesService);
  private txSvc = inject(TransactionsService);
  private rulesSvc = inject(CategoryRulesService);
  private dialog = inject(MatDialog);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private snack = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);

  detail = signal<ImportBatchDetailDto | null>(null);
  uploadPreview = signal<ImportPreviewDto | null>(null);
  loading = signal(true);
  categories = signal<CategoryDto[]>([]);
  columns = ['date', 'description', 'amount', 'category', 'actions'];
  dupColumns = ['date', 'description', 'amount'];

  // Local mutable list of new rows (enables inline edits without full reload)
  private newRowsLocal = signal<ImportPreviewRowDto[]>([]);

  newRows = computed<ImportPreviewRowDto[]>(() => {
    if (this.newRowsLocal().length > 0) return this.newRowsLocal();
    const up = this.uploadPreview();
    if (up) return up.sample.filter(r => !r.isDuplicate);
    return this.detail()?.transactions.filter(r => !r.isDuplicate) ?? [];
  });

  duplicateRows = computed<ImportPreviewRowDto[]>(() => {
    const up = this.uploadPreview();
    return up ? up.sample.filter(r => r.isDuplicate) : [];
  });

  uncategorizedCount = computed(() =>
    this.newRows().filter(r => !r.categoryId).length
  );

  private id = toSignal(this.route.paramMap.pipe(map(p => p.get('id') ?? '')), { initialValue: '' });
  private pollInterval: ReturnType<typeof setInterval> | null = null;

  constructor() {
    const nav = this.router.getCurrentNavigation();
    const stateUpload = nav?.extras?.state?.['preview'] as ImportPreviewDto | undefined
      ?? (history.state as { preview?: ImportPreviewDto } | undefined)?.preview;
    if (stateUpload) {
      this.uploadPreview.set(stateUpload);
      this.newRowsLocal.set(stateUpload.sample.filter(r => !r.isDuplicate));
    }

    this.categoriesSvc.list().pipe(takeUntilDestroyed()).subscribe({
      next: c => this.categories.set(c),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });

    effect(() => {
      const id = this.id();
      if (!id) return;
      this.load(id);
    });
  }

  private load(id: string) {
    this.loading.set(true);
    this.svc.get(id).subscribe({
      next: d => {
        this.detail.set(d);
        this.loading.set(false);
        // Sync local rows from DB detail (after rerun-rules or llm suggest)
        if (this.newRowsLocal().length === 0)
          this.newRowsLocal.set(d.transactions.filter(r => !r.isDuplicate));
        this.managePoll(d.batch);
      },
      error: e => {
        this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 });
        this.loading.set(false);
      },
    });
  }

  private managePoll(batch: ImportBatchDto) {
    if (batch.llmStatus === 'Running') {
      if (!this.pollInterval) {
        this.pollInterval = setInterval(() => {
          const id = this.id();
          if (!id) return;
          this.svc.get(id).subscribe({
            next: d => {
              this.detail.set(d);
              if (d.batch.llmStatus !== 'Running') {
                this.clearPoll();
                this.newRowsLocal.set(d.transactions.filter(r => !r.isDuplicate));
              }
            },
          });
        }, 2000);
      }
    } else {
      this.clearPoll();
    }
  }

  private clearPoll() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
  }

  onCategorySelection(row: ImportPreviewRowDto, selection: CategorySelection) {
    if (!row.transactionId) return;
    this.txSvc.updateCategory(row.transactionId, selection.categoryId, selection.subCategoryId).subscribe({
      next: updated => this.applyUpdate(row, updated),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  private applyUpdate(row: ImportPreviewRowDto, updated: { categoryId: string | null; subCategoryId: string | null; categorizationSource: ImportPreviewRowDto['categorizationSource']; needsReview: boolean }) {
    this.newRowsLocal.update(rows =>
      rows.map(r => r === row
        ? { ...r, categoryId: updated.categoryId, subCategoryId: updated.subCategoryId, categorizationSource: updated.categorizationSource, needsReview: updated.needsReview }
        : r)
    );
  }

  rerunRules() {
    const id = this.id();
    this.svc.rerunRules(id).subscribe({
      next: () => {
        this.newRowsLocal.set([]);
        this.load(id);
      },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  suggestLlm() {
    const id = this.id();
    this.svc.suggestLlm(id).subscribe({
      next: () => this.load(id),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  commit() {
    const id = this.id();
    this.svc.commit(id).subscribe({
      next: () => { this.clearPoll(); this.router.navigate(['/ledger']); },
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

  createRuleFrom(row: ImportPreviewRowDto) {
    const prefill: Partial<SaveCategoryRuleRequest> = {
      matchField: 'Description',
      matchType: 'Contains',
      pattern: row.description,
      targetCategoryId: row.categoryId ?? '',
      targetSubCategoryId: row.subCategoryId ?? null,
      scope: 'Family',
      priority: 10,
      isEnabled: true,
    };
    this.dialog.open(RuleEditDialog, { data: { prefill }, width: '480px' })
      .afterClosed().subscribe((req: SaveCategoryRuleRequest | undefined) => {
        if (!req) return;
        this.rulesSvc.create(req).subscribe({
          next: () => {
            this.snack.open('Rule created — re-running rules…', 'Close', { duration: 3000 });
            this.rerunRules();
          },
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  discard() {
    const id = this.id();
    this.svc.discard(id).subscribe({
      next: () => { this.clearPoll(); this.router.navigate(['/imports']); },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }
}
