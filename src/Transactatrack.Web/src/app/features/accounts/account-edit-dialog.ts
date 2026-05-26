import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OwnersService, OwnerDto } from '../owners/owners.service';
import { ImportsService, BankDto } from '../imports/imports.service';
import { ACCOUNT_TYPES, AccountType, AccountDto } from './accounts.service';

export interface AccountEditDialogData {
  account?: AccountDto;
}

@Component({
  selector: 'app-account-edit-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.account ? 'Edit Account' : 'New Account' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Owner</mat-label>
          <mat-select formControlName="ownerId">
            @for (o of owners(); track o.id) {
              <mat-option [value]="o.id">{{ o.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Institution</mat-label>
          <input matInput formControlName="institution" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Account Type</mat-label>
          <mat-select formControlName="accountType">
            @for (t of accountTypes; track t) {
              <mat-option [value]="t">{{ t }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Bank Code</mat-label>
          <mat-select formControlName="bankCode">
            <mat-option [value]="''">(none)</mat-option>
            @for (b of banks(); track b.bankCode) {
              <mat-option [value]="b.bankCode">{{ b.bankCode }}</mat-option>
            }
            @if (orphanCode()) {
              <mat-option [value]="orphanCode()!">{{ orphanCode() }} (unrecognized)</mat-option>
            }
          </mat-select>
          <mat-hint>Determines which parser handles file uploads for this account.</mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="form.invalid" (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
  styles: ['.full-width { width: 100%; } .form-grid { display: flex; flex-direction: column; min-width: 360px; } mat-dialog-content { padding-top: 8px; }'],
})
export class AccountEditDialog {
  data: AccountEditDialogData = inject(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<AccountEditDialog>);
  private ownersSvc = inject(OwnersService);
  private importsSvc = inject(ImportsService);

  accountTypes = ACCOUNT_TYPES;
  owners = toSignal(this.ownersSvc.list(), { initialValue: [] as OwnerDto[] });
  banks = toSignal(this.importsSvc.listBanks(), { initialValue: [] as BankDto[] });

  // If the account already has a bankCode that no registered parser advertises,
  // surface it as an "(unrecognized)" option so editing doesn't silently drop it.
  orphanCode = computed(() => {
    const current = this.data.account?.bankCode ?? '';
    if (!current) return null;
    return this.banks().some(b => b.bankCode === current) ? null : current;
  });

  form = new FormGroup({
    ownerId: new FormControl<string>(this.data.account?.ownerId ?? '', Validators.required),
    name: new FormControl(this.data.account?.name ?? '', [Validators.required, Validators.maxLength(200)]),
    institution: new FormControl(this.data.account?.institution ?? ''),
    accountType: new FormControl<AccountType>(this.data.account?.accountType ?? 'Checking', Validators.required),
    bankCode: new FormControl(this.data.account?.bankCode ?? ''),
  });

  save() {
    if (this.form.valid) {
      // Persist null when the user picks "(none)" so the DTO stays consistent.
      const value = { ...this.form.value, bankCode: this.form.value.bankCode || null };
      this.ref.close(value);
    }
  }
}
