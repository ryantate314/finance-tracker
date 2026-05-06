import { Component, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamilyContextService } from '../../core/family-context/family-context.service';
import { AccountDto, AccountsService, CreateAccountRequest } from './accounts.service';
import { AccountEditDialog } from './account-edit-dialog';

@Component({
  selector: 'app-accounts-list',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule],
  template: `
    <div class="page-header">
      <h2>Accounts</h2>
      <button mat-flat-button (click)="openNew()">New</button>
    </div>
    <mat-table [dataSource]="accounts()">
      <ng-container matColumnDef="name">
        <mat-header-cell *matHeaderCellDef>Name</mat-header-cell>
        <mat-cell *matCellDef="let a">{{ a.name }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="institution">
        <mat-header-cell *matHeaderCellDef>Institution</mat-header-cell>
        <mat-cell *matCellDef="let a">{{ a.institution }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="accountType">
        <mat-header-cell *matHeaderCellDef>Type</mat-header-cell>
        <mat-cell *matCellDef="let a">{{ a.accountType }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="actions">
        <mat-header-cell *matHeaderCellDef></mat-header-cell>
        <mat-cell *matCellDef="let a">
          <button mat-icon-button aria-label="Edit account" (click)="openEdit(a)"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button aria-label="Delete account" (click)="delete(a)"><mat-icon>delete</mat-icon></button>
        </mat-cell>
      </ng-container>
      <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
      <mat-row *matRowDef="let row; columns: columns"></mat-row>
    </mat-table>
  `,
  styles: ['.page-header { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; }'],
})
export class AccountsList {
  private svc = inject(AccountsService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private familyCtx = inject(FamilyContextService);

  accounts = signal<AccountDto[]>([]);
  columns = ['name', 'institution', 'accountType', 'actions'];

  constructor() {
    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      this.load();
    });
  }

  load() {
    this.svc.list().subscribe(a => this.accounts.set(a));
  }

  openNew() {
    this.dialog.open(AccountEditDialog, { data: {}, width: '440px' })
      .afterClosed().subscribe((val: CreateAccountRequest | undefined) => {
        if (!val) return;
        this.svc.create(val).subscribe({
          next: () => this.load(),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  openEdit(account: AccountDto) {
    this.dialog.open(AccountEditDialog, { data: { account }, width: '440px' })
      .afterClosed().subscribe((val: CreateAccountRequest | undefined) => {
        if (!val) return;
        this.svc.update(account.id, val).subscribe({
          next: () => this.load(),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  delete(account: AccountDto) {
    this.svc.delete(account.id).subscribe({
      next: () => this.load(),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }
}
