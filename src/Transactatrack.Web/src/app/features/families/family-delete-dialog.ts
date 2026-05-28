import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { FamilyDeleteImpactDto } from './families.service';

@Component({
  selector: 'app-family-delete-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Delete {{ data.familyName }}?</h2>
    <mat-dialog-content>
      @if (isEmpty()) {
        <p>This family has no data. It will be permanently removed.</p>
      } @else {
        <p>This will <strong>permanently delete</strong> the family and all of its data:</p>
        <ul>
          @if (data.owners)         { <li>{{ data.owners }} owner(s)</li> }
          @if (data.accounts)       { <li>{{ data.accounts }} account(s)</li> }
          @if (data.categories)     { <li>{{ data.categories }} categor(y/ies)</li> }
          @if (data.subCategories)  { <li>{{ data.subCategories }} sub-categor(y/ies)</li> }
          @if (data.categoryRules)  { <li>{{ data.categoryRules }} rule(s)</li> }
          @if (data.importBatches)  { <li>{{ data.importBatches }} import batch(es)</li> }
          @if (data.transactions)   { <li>{{ data.transactions }} transaction(s)</li> }
        </ul>
        <p>This cannot be undone.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(false)">Cancel</button>
      <button mat-flat-button color="warn" (click)="ref.close(true)">Delete</button>
    </mat-dialog-actions>
  `,
  styles: ['mat-dialog-content { min-width: 360px; } ul { margin: 8px 0 16px; }'],
})
export class FamilyDeleteDialog {
  data: FamilyDeleteImpactDto = inject(MAT_DIALOG_DATA);
  ref = inject(MatDialogRef<FamilyDeleteDialog, boolean>);

  isEmpty(): boolean {
    const d = this.data;
    return !(d.owners || d.accounts || d.categories || d.subCategories
      || d.categoryRules || d.importBatches || d.transactions);
  }
}
