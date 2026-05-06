import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { extractErrorMessage } from '../../core/api/api-error';
import { CategoriesService } from '../categories/categories.service';
import { AccountsService } from '../accounts/accounts.service';
import { CategoryRuleDto, CategoryRulesService } from './category-rules.service';
import { RuleEditDialog } from './rule-edit-dialog';

@Component({
  selector: 'app-rules-page',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule, MatSlideToggleModule, DragDropModule],
  template: `
    <div class="page-header">
      <h2>Category Rules</h2>
      <button mat-flat-button (click)="openNew()">+ New Rule</button>
    </div>

    <mat-table
      [dataSource]="rules()"
      cdkDropList
      (cdkDropListDropped)="onDrop($event)"
      class="rules-table">

      <ng-container matColumnDef="drag">
        <mat-header-cell *matHeaderCellDef></mat-header-cell>
        <mat-cell *matCellDef="let r" cdkDragHandle>
          <mat-icon class="drag-handle">drag_indicator</mat-icon>
        </mat-cell>
      </ng-container>

      <ng-container matColumnDef="priority">
        <mat-header-cell *matHeaderCellDef>Priority</mat-header-cell>
        <mat-cell *matCellDef="let r">{{ r.priority }}</mat-cell>
      </ng-container>

      <ng-container matColumnDef="match">
        <mat-header-cell *matHeaderCellDef>Match</mat-header-cell>
        <mat-cell *matCellDef="let r">
          <span class="chip">{{ r.matchField }}</span>
          @if (r.matchField !== 'AmountRange') {
            <span class="chip muted">{{ r.matchType }}</span>
          }
        </mat-cell>
      </ng-container>

      <ng-container matColumnDef="pattern">
        <mat-header-cell *matHeaderCellDef>Pattern / Range</mat-header-cell>
        <mat-cell *matCellDef="let r">
          @if (r.matchField === 'AmountRange') {
            {{ r.amountMin ?? '—' }} .. {{ r.amountMax ?? '—' }}
          } @else {
            {{ r.pattern }}
          }
        </mat-cell>
      </ng-container>

      <ng-container matColumnDef="category">
        <mat-header-cell *matHeaderCellDef>Category</mat-header-cell>
        <mat-cell *matCellDef="let r">{{ targetLabel(r) }}</mat-cell>
      </ng-container>

      <ng-container matColumnDef="scope">
        <mat-header-cell *matHeaderCellDef>Scope</mat-header-cell>
        <mat-cell *matCellDef="let r">
          {{ r.scope === 'Account' ? (accountName(r.accountId!) + ' (acct)') : 'Family' }}
        </mat-cell>
      </ng-container>

      <ng-container matColumnDef="enabled">
        <mat-header-cell *matHeaderCellDef>Enabled</mat-header-cell>
        <mat-cell *matCellDef="let r">
          <mat-slide-toggle
            [checked]="r.isEnabled"
            (change)="toggleEnabled(r)">
          </mat-slide-toggle>
        </mat-cell>
      </ng-container>

      <ng-container matColumnDef="actions">
        <mat-header-cell *matHeaderCellDef></mat-header-cell>
        <mat-cell *matCellDef="let r">
          <button mat-icon-button (click)="openEdit(r)"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button color="warn" (click)="deleteRule(r)"><mat-icon>delete</mat-icon></button>
        </mat-cell>
      </ng-container>

      <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
      <mat-row *matRowDef="let row; columns: columns" cdkDrag></mat-row>
    </mat-table>

    @if (rules().length === 0) {
      <p class="empty-msg">No rules yet. Click "+ New Rule" to add one.</p>
    }
  `,
  styles: [`
    .page-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 0; }
    .rules-table { width: 100%; }
    .drag-handle { cursor: move; color: rgba(0,0,0,0.38); }
    .chip { padding: 2px 6px; border-radius: 4px; font-size: 0.75rem; background: rgba(0,0,0,0.08); margin-right: 4px; }
    .muted { color: rgba(0,0,0,0.55); }
    .empty-msg { color: rgba(0,0,0,0.55); padding: 16px 0; }
  `],
})
export class RulesPage {
  private svc = inject(CategoryRulesService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private categoriesSvc = inject(CategoriesService);
  private accountsSvc = inject(AccountsService);

  rules = signal<CategoryRuleDto[]>([]);
  columns = ['drag', 'priority', 'match', 'pattern', 'category', 'scope', 'enabled', 'actions'];

  private categoriesMap = new Map<string, string>();
  private subCategoriesMap = new Map<string, string>();
  private accountsMap = new Map<string, string>();

  constructor() {
    this.load();
    this.categoriesSvc.list().pipe(takeUntilDestroyed()).subscribe(cats => {
      cats.forEach(c => {
        this.categoriesMap.set(c.id, c.name);
        c.subCategories.forEach(s => this.subCategoriesMap.set(s.id, s.name));
      });
    });
    this.accountsSvc.list().pipe(takeUntilDestroyed()).subscribe(accts =>
      accts.forEach(a => this.accountsMap.set(a.id, a.name)));
  }

  private load() {
    this.svc.list().subscribe({
      next: r => this.rules.set(r),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  categoryName(id: string) { return this.categoriesMap.get(id) ?? id; }
  subCategoryName(id: string) { return this.subCategoriesMap.get(id) ?? id; }
  accountName(id: string) { return this.accountsMap.get(id) ?? id; }

  targetLabel(r: CategoryRuleDto): string {
    const cat = this.categoryName(r.targetCategoryId);
    return r.targetSubCategoryId ? `${cat} › ${this.subCategoryName(r.targetSubCategoryId)}` : cat;
  }

  openNew() {
    this.dialog.open(RuleEditDialog, { data: {} }).afterClosed().subscribe(req => {
      if (!req) return;
      this.svc.create(req).subscribe({
        next: () => this.load(),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
    });
  }

  openEdit(rule: CategoryRuleDto) {
    this.dialog.open(RuleEditDialog, { data: { rule } }).afterClosed().subscribe(req => {
      if (!req) return;
      this.svc.update(rule.id, req).subscribe({
        next: () => this.load(),
        error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
      });
    });
  }

  deleteRule(rule: CategoryRuleDto) {
    this.svc.delete(rule.id).subscribe({
      next: () => this.rules.update(rs => rs.filter(r => r.id !== rule.id)),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  toggleEnabled(rule: CategoryRuleDto) {
    const updated = { ...rule, isEnabled: !rule.isEnabled };
    this.svc.update(rule.id, updated).subscribe({
      next: () => this.rules.update(rs => rs.map(r => r.id === rule.id ? { ...r, isEnabled: !r.isEnabled } : r)),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  onDrop(event: CdkDragDrop<CategoryRuleDto[]>) {
    const list = [...this.rules()];
    moveItemInArray(list, event.previousIndex, event.currentIndex);
    this.rules.set(list);

    const updates = list.map((r, i) => ({ id: r.id, priority: (i + 1) * 10 }));
    this.svc.reorder(updates).subscribe({
      next: () => this.load(),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }
}
