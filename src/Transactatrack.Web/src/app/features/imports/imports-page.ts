import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamilyContextService } from '../../core/family-context/family-context.service';
import { ImportBatchDto, ImportsService } from './imports.service';
import { ImportUploadDialog } from './import-upload-dialog';

@Component({
  selector: 'app-imports-page',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule, RouterLink, DatePipe],
  template: `
    <div class="page-header">
      <h2>Imports</h2>
      <button mat-flat-button (click)="openNew()">New Import</button>
    </div>
    <mat-table [dataSource]="batches()">
      <ng-container matColumnDef="filename">
        <mat-header-cell *matHeaderCellDef>File</mat-header-cell>
        <mat-cell *matCellDef="let b">
          <a [routerLink]="['/imports', b.id]">{{ b.originalFilename }}</a>
        </mat-cell>
      </ng-container>
      <ng-container matColumnDef="bankCode">
        <mat-header-cell *matHeaderCellDef>Bank</mat-header-cell>
        <mat-cell *matCellDef="let b">{{ b.bankCode }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="status">
        <mat-header-cell *matHeaderCellDef>Status</mat-header-cell>
        <mat-cell *matCellDef="let b">
          <span class="status-pill" [class.pending]="b.status === 'Pending'" [class.committed]="b.status === 'Committed'">
            {{ b.status }}
          </span>
        </mat-cell>
      </ng-container>
      <ng-container matColumnDef="rows">
        <mat-header-cell *matHeaderCellDef>Rows</mat-header-cell>
        <mat-cell *matCellDef="let b">{{ b.transactionCount }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="uploaded">
        <mat-header-cell *matHeaderCellDef>Uploaded</mat-header-cell>
        <mat-cell *matCellDef="let b">{{ b.uploadedUtc | date:'short' }}</mat-cell>
      </ng-container>
      <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
      <mat-row *matRowDef="let row; columns: columns"></mat-row>
    </mat-table>
    @if (batches().length === 0) {
      <p class="empty">No imports yet. Click "New Import" to upload a CSV.</p>
    }
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; }
    .empty { color: rgba(0,0,0,0.55); padding: 16px 0; }
    .status-pill { padding: 2px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: 600; }
    .status-pill.pending { background: #fff3cd; color: #856404; }
    .status-pill.committed { background: #d4edda; color: #155724; }
  `],
})
export class ImportsPage {
  private svc = inject(ImportsService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private router = inject(Router);
  private familyCtx = inject(FamilyContextService);

  batches = signal<ImportBatchDto[]>([]);
  columns = ['filename', 'bankCode', 'status', 'rows', 'uploaded'];

  constructor() {
    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      this.load();
    });
  }

  load() {
    this.svc.list().subscribe(b => this.batches.set(b));
  }

  openNew() {
    this.dialog.open(ImportUploadDialog, { width: '480px' })
      .afterClosed().subscribe((val: { accountId: string; file: File } | undefined) => {
        if (!val) return;
        this.svc.upload(val.accountId, val.file).subscribe({
          // Pass the full preview (which includes dropped duplicate rows) via router state
          // so the preview page can show what was deduped — that information isn't in the DB.
          next: preview => this.router.navigate(['/imports', preview.batchId], { state: { preview } }),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }
}
