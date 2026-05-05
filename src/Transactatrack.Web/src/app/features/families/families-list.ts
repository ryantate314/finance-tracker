import { Component, OnInit, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamiliesService, FamilyDto } from './families.service';
import { FamilyEditDialog } from './family-edit-dialog';

@Component({
  selector: 'app-families-list',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatIconModule],
  template: `
    <div class="page-header">
      <h2>Families</h2>
      <button mat-flat-button (click)="openNew()">New</button>
    </div>
    <mat-table [dataSource]="families() ?? []">
      <ng-container matColumnDef="name">
        <mat-header-cell *matHeaderCellDef>Name</mat-header-cell>
        <mat-cell *matCellDef="let f">{{ f.name }}</mat-cell>
      </ng-container>
      <ng-container matColumnDef="actions">
        <mat-header-cell *matHeaderCellDef></mat-header-cell>
        <mat-cell *matCellDef="let f">
          <button mat-icon-button aria-label="Edit family" (click)="openEdit(f)"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button aria-label="Delete family" (click)="delete(f)"><mat-icon>delete</mat-icon></button>
        </mat-cell>
      </ng-container>
      <mat-header-row *matHeaderRowDef="columns"></mat-header-row>
      <mat-row *matRowDef="let row; columns: columns"></mat-row>
    </mat-table>
  `,
  styles: ['.page-header { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; }'],
})
export class FamiliesList implements OnInit {
  private svc = inject(FamiliesService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);

  families = toSignal(this.svc.families$);
  columns = ['name', 'actions'];

  ngOnInit() { this.svc.refresh().subscribe(); }

  openNew() {
    this.dialog.open(FamilyEditDialog, { data: {}, width: '400px' })
      .afterClosed().subscribe(name => {
        if (!name) return;
        this.svc.create(name).subscribe({
          next: () => this.svc.refresh().subscribe(),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  openEdit(family: FamilyDto) {
    this.dialog.open(FamilyEditDialog, { data: { name: family.name }, width: '400px' })
      .afterClosed().subscribe(name => {
        if (!name) return;
        this.svc.update(family.id, name).subscribe({
          next: () => this.svc.refresh().subscribe(),
          error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
        });
      });
  }

  delete(family: FamilyDto) {
    this.svc.delete(family.id).subscribe({
      next: () => this.svc.refresh().subscribe(),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }
}
