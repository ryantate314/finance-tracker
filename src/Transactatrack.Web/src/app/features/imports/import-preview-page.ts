import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs/operators';
import { extractErrorMessage } from '../../core/api/api-error';
import { ImportBatchDetailDto, ImportPreviewDto, ImportPreviewRowDto, ImportsService } from './imports.service';

@Component({
  selector: 'app-import-preview-page',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule, DatePipe, DecimalPipe],
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
          <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
          <mat-row *matRowDef="let row; columns: columns"></mat-row>
        </mat-table>
      }

      @if (duplicateRows().length > 0) {
        <h3 class="dup-heading">Duplicate rows (skipped)</h3>
        <p class="muted">
          These rows already match a transaction in this account (or another row in this same upload)
          and were not imported.
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
          <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
          <mat-row *matRowDef="let row; columns: columns"></mat-row>
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
    .actions { display: flex; gap: 8px; justify-content: flex-end; padding: 16px 0; }
    .right { justify-content: flex-end; text-align: right; }
    .debit { color: #b00020; }
    .dup-heading { margin-top: 24px; color: #856404; }
    .dup-table mat-row { background: #fff8e6; }
    .muted { color: rgba(0,0,0,0.55); margin: 0 0 8px; }
  `],
})
export class ImportPreviewPage {
  private svc = inject(ImportsService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private snack = inject(MatSnackBar);

  detail = signal<ImportBatchDetailDto | null>(null);
  uploadPreview = signal<ImportPreviewDto | null>(null);
  loading = signal(true);
  columns = ['date', 'description', 'amount'];

  newRows = computed<ImportPreviewRowDto[]>(() => {
    const up = this.uploadPreview();
    if (up) return up.sample.filter(r => !r.isDuplicate);
    return this.detail()?.transactions ?? [];
  });

  duplicateRows = computed<ImportPreviewRowDto[]>(() => {
    const up = this.uploadPreview();
    return up ? up.sample.filter(r => r.isDuplicate) : [];
  });

  private id = toSignal(this.route.paramMap.pipe(map(p => p.get('id') ?? '')), { initialValue: '' });

  constructor() {
    // If we just uploaded, the upload response (with dropped duplicates) was passed via
    // router state. Capture it once on mount — it's gone on a hard refresh.
    const nav = this.router.getCurrentNavigation();
    const stateUpload = nav?.extras?.state?.['preview'] as ImportPreviewDto | undefined
      ?? (history.state as { preview?: ImportPreviewDto } | undefined)?.preview;
    if (stateUpload) this.uploadPreview.set(stateUpload);

    effect(() => {
      const id = this.id();
      if (!id) return;
      this.load(id);
    });
  }

  private load(id: string) {
    this.loading.set(true);
    this.svc.get(id).subscribe({
      next: d => { this.detail.set(d); this.loading.set(false); },
      error: e => { this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }); this.loading.set(false); },
    });
  }

  commit() {
    const id = this.id();
    this.svc.commit(id).subscribe({
      next: () => this.router.navigate(['/ledger']),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  discard() {
    const id = this.id();
    this.svc.discard(id).subscribe({
      next: () => this.router.navigate(['/imports']),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }
}
