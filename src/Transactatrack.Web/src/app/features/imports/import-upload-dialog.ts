import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { map } from 'rxjs/operators';
import { AccountDto, AccountsService } from '../accounts/accounts.service';

@Component({
  selector: 'app-import-upload-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New Import</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Account</mat-label>
          <mat-select formControlName="accountId">
            @for (a of accounts(); track a.id) {
              <mat-option [value]="a.id">{{ a.name }} <span class="muted">({{ a.bankCode }})</span></mat-option>
            }
          </mat-select>
          @if (accounts().length === 0) {
            <mat-hint>No accounts have a BankCode set. Edit an account to add one.</mat-hint>
          }
        </mat-form-field>

        <div class="file-row">
          <button mat-stroked-button type="button" (click)="picker.click()">Choose CSV…</button>
          <input #picker type="file" accept=".csv,text/csv" hidden (change)="onFileSelected($event)" />
          @if (selectedFile()) {
            <span class="filename">{{ selectedFile()!.name }}</span>
          } @else {
            <span class="muted">No file selected</span>
          }
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="!canUpload()" (click)="upload()">Upload</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; }
    .form-grid { display: flex; flex-direction: column; gap: 12px; min-width: 420px; }
    .file-row { display: flex; align-items: center; gap: 12px; }
    .filename { font-weight: 500; }
    .muted { color: rgba(0,0,0,0.55); }
    mat-dialog-content { padding-top: 8px; overflow: visible; }
  `],
})
export class ImportUploadDialog {
  private ref = inject(MatDialogRef<ImportUploadDialog>);
  private accountsSvc = inject(AccountsService);

  accounts = toSignal(
    this.accountsSvc.list().pipe(
      map((all: AccountDto[]) => all.filter(a => !!a.bankCode))
    ),
    { initialValue: [] as AccountDto[] }
  );

  selectedFile = signal<File | null>(null);

  form = new FormGroup({
    accountId: new FormControl<string>('', Validators.required),
  });

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  canUpload(): boolean {
    return this.form.valid && this.selectedFile() != null;
  }

  upload() {
    if (!this.canUpload()) return;
    this.ref.close({
      accountId: this.form.value.accountId!,
      file: this.selectedFile()!,
    });
  }
}
