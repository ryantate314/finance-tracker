import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TransactionDto } from '../ledger/ledger.service';
import { LedgerService } from '../ledger/ledger.service';

export interface LinkTransferData {
  source: TransactionDto;
  accountName: (id: string) => string;
}

@Component({
  selector: 'app-link-transfer-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatProgressBarModule, DatePipe, DecimalPipe],
  template: `
    <h2 mat-dialog-title>Link as transfer</h2>
    <mat-dialog-content>
      <p class="src">
        Pairing <b>{{ data.source.description }}</b>
        ({{ data.source.amount | number:'1.2-2' }}, {{ data.accountName(data.source.accountId) }})
        with an opposite-amount transaction from another account.
      </p>

      @if (loading()) {
        <mat-progress-bar mode="indeterminate"></mat-progress-bar>
      } @else if (candidates().length === 0) {
        <p class="empty">No unpaired transaction of {{ -data.source.amount | number:'1.2-2' }}
          found in another account within ±14 days.</p>
      } @else {
        <ul class="cands">
          @for (c of candidates(); track c.id) {
            <li [class.selected]="selectedId() === c.id" (click)="selectedId.set(c.id)">
              <div class="row1">
                <span class="desc">{{ c.description }}</span>
                <span class="amt" [class.debit]="c.amount < 0">{{ c.amount | number:'1.2-2' }}</span>
              </div>
              <div class="row2 muted">{{ c.date | date:'shortDate' }} · {{ accountName(c.accountId) }}</div>
            </li>
          }
        </ul>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="primary" [disabled]="!selectedId()" (click)="confirm()">Link</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .src { margin: 0 0 12px; }
    .empty { color: rgba(0,0,0,0.55); }
    .cands { list-style: none; margin: 0; padding: 0; max-height: 320px; overflow: auto; }
    .cands li { padding: 8px 10px; border: 1px solid rgba(0,0,0,0.12); border-radius: 6px; margin-bottom: 6px; cursor: pointer; }
    .cands li.selected { border-color: #1976d2; background: #e3f2fd; }
    .row1 { display: flex; justify-content: space-between; gap: 12px; }
    .desc { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .amt { font-variant-numeric: tabular-nums; }
    .amt.debit { color: #b00020; }
    .muted { color: rgba(0,0,0,0.55); font-size: 0.85rem; }
  `],
})
export class LinkTransferDialog {
  private ref = inject(MatDialogRef<LinkTransferDialog, string>);
  private ledger = inject(LedgerService);
  data = inject<LinkTransferData>(MAT_DIALOG_DATA);

  candidates = signal<TransactionDto[]>([]);
  loading = signal(true);
  selectedId = signal<string | null>(null);

  constructor() {
    const src = this.data.source;
    const center = new Date(src.date);
    const from = new Date(center);
    from.setDate(from.getDate() - 14);
    const to = new Date(center);
    to.setDate(to.getDate() + 14);

    const opposite = -src.amount;
    this.ledger.list({ from, to, page: 1, pageSize: 200 }).subscribe({
      next: r => {
        this.candidates.set(
          r.items.filter(
            t =>
              t.id !== src.id &&
              t.accountId !== src.accountId &&
              !t.isTransfer &&
              !t.transferGroupId &&
              t.amount === opposite,
          ),
        );
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  accountName(id: string): string {
    return this.data.accountName(id);
  }

  confirm() {
    const id = this.selectedId();
    if (id) this.ref.close(id);
  }
}
