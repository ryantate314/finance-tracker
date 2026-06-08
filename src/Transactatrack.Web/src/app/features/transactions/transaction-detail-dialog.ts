import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { CategoryPicker, CategorySelection } from '../categories/category-picker.component';
import { CategoryDto } from '../categories/categories.service';
import { TransactionDto } from '../ledger/ledger.service';

export interface TransactionDetailDialogData {
  tx: TransactionDto;
  categories: CategoryDto[];
  accountName: string;
}

export interface TransactionDetailResult {
  categoryId: string | null;
  subCategoryId: string | null;
  note: string | null;
}

@Component({
  selector: 'app-transaction-detail-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, CategoryPicker, DatePipe, DecimalPipe,
  ],
  template: `
    <h2 mat-dialog-title>Transaction details</h2>
    <mat-dialog-content>
      <dl class="detail-grid">
        <dt>Date</dt>
        <dd>{{ data.tx.date | date:'mediumDate' }}</dd>
        <dt>Amount</dt>
        <dd [class.debit]="data.tx.amount < 0">{{ data.tx.amount | number:'1.2-2' }}</dd>
        <dt>Account</dt>
        <dd>{{ data.accountName }}</dd>
        <dt>Description</dt>
        <dd>{{ data.tx.description }}</dd>
        @if (data.tx.merchant) {
          <dt>Merchant</dt>
          <dd>{{ data.tx.merchant }}</dd>
        }
      </dl>

      <label class="field-label" for="tx-category">Category</label>
      <div class="picker-wrap">
        <app-category-picker
          [categories]="data.categories"
          [categoryId]="categoryId()"
          [subCategoryId]="subCategoryId()"
          (selectionChange)="onCategorySelection($event)">
        </app-category-picker>
      </div>

      <mat-form-field appearance="outline" class="note-field">
        <mat-label>Note</mat-label>
        <textarea matInput rows="4" [formControl]="noteCtrl" maxlength="1000"
          placeholder="What was this transaction really for?"></textarea>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { padding-top: 8px; min-width: 420px; }
    .detail-grid { display: grid; grid-template-columns: auto 1fr; gap: 4px 16px; margin: 0 0 16px; }
    .detail-grid dt { color: rgba(0,0,0,0.55); font-size: 0.8125rem; }
    .detail-grid dd { margin: 0; }
    .detail-grid dd.debit { color: #b00020; }
    .field-label { display: block; color: rgba(0,0,0,0.55); font-size: 0.8125rem; margin-bottom: 2px; }
    .picker-wrap { border: 1px solid rgba(0,0,0,0.18); border-radius: 4px; margin-bottom: 16px; padding: 0 4px; }
    .note-field { width: 100%; }
  `],
})
export class TransactionDetailDialog {
  data = inject<TransactionDetailDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<TransactionDetailDialog, TransactionDetailResult>);

  categoryId = signal<string | null>(this.data.tx.categoryId);
  subCategoryId = signal<string | null>(this.data.tx.subCategoryId);
  noteCtrl = new FormControl(this.data.tx.note ?? '', { nonNullable: true, validators: [Validators.maxLength(1000)] });

  onCategorySelection(selection: CategorySelection) {
    this.categoryId.set(selection.categoryId);
    this.subCategoryId.set(selection.subCategoryId);
  }

  save() {
    const note = this.noteCtrl.value.trim();
    this.ref.close({
      categoryId: this.categoryId(),
      subCategoryId: this.subCategoryId(),
      note: note.length > 0 ? note : null,
    });
  }
}
