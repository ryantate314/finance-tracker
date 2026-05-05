import { Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface FamilyEditDialogData {
  name?: string;
}

@Component({
  selector: 'app-family-edit-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.name ? 'Edit Family' : 'New Family' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="form.invalid" (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
  styles: ['.full-width { width: 100%; } mat-dialog-content { padding-top: 8px; overflow: visible; }'],
})
export class FamilyEditDialog {
  data: FamilyEditDialogData = inject(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<FamilyEditDialog>);

  form = new FormGroup({
    name: new FormControl(this.data.name ?? '', [Validators.required, Validators.maxLength(200)]),
  });

  save() {
    if (this.form.valid) this.ref.close(this.form.value.name);
  }
}
