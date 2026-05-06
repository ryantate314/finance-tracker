import { Component, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamilyContextService } from '../../core/family-context/family-context.service';
import { OwnerDto, OwnersService } from './owners.service';
import { OwnerEditDialog } from './owner-edit-dialog';

@Component({
  selector: 'app-owners-list',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule],
  template: `
    <div class="page-header">
      <h2>Owners</h2>
      <button mat-flat-button (click)="openNew()">New</button>
    </div>
    <mat-table [dataSource]="owners()">
      <ng-container matColumnDef="name">
        <mat-header-cell *matHeaderCellDef>Name</mat-header-cell>
        <mat-cell *matCellDef="let o">{{ o.name }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="actions">
        <mat-header-cell *matHeaderCellDef></mat-header-cell>
        <mat-cell *matCellDef="let o">
          <button mat-icon-button aria-label="Edit owner" (click)="openEdit(o)"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button aria-label="Delete owner" (click)="delete(o)"><mat-icon>delete</mat-icon></button>
        </mat-cell>
      </ng-container>
      <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
      <mat-row *matRowDef="let row; columns: columns"></mat-row>
    </mat-table>
  `,
  styles: ['.page-header { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; }'],
})
export class OwnersList {
  private svc = inject(OwnersService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private familyCtx = inject(FamilyContextService);

  owners = signal<OwnerDto[]>([]);
  columns = ['name', 'actions'];

  constructor() {
    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      this.load();
    });
  }

  load() {
    this.svc.list().subscribe(o => this.owners.set(o));
  }

  openNew() {
    this.dialog.open(OwnerEditDialog, { data: {}, width: '400px' })
      .afterClosed().subscribe(name => {
        if (!name) return;
        this.svc.create(name).subscribe({
          next: () => this.load(),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  openEdit(owner: OwnerDto) {
    this.dialog.open(OwnerEditDialog, { data: { name: owner.name }, width: '400px' })
      .afterClosed().subscribe(name => {
        if (!name) return;
        this.svc.update(owner.id, name).subscribe({
          next: () => this.load(),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  delete(owner: OwnerDto) {
    this.svc.delete(owner.id).subscribe({
      next: () => this.load(),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }
}
