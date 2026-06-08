import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface NoteEditDialogData {
  note: string | null;
  description: string;
}

@Component({
  selector: 'app-note-edit-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Note</h2>
    <mat-dialog-content>
      <p class="desc">{{ data.description }}</p>
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
    mat-dialog-content { padding-top: 8px; min-width: 380px; }
    .desc { margin: 0 0 12px; color: rgba(0,0,0,0.6); font-size: 0.875rem; }
    .note-field { width: 100%; }
  `],
})
export class NoteEditDialog {
  data = inject<NoteEditDialogData>(MAT_DIALOG_DATA);
  // afterClosed: undefined = cancelled, otherwise the new note (null when cleared).
  private ref = inject(MatDialogRef<NoteEditDialog, string | null>);

  noteCtrl = new FormControl(this.data.note ?? '', { nonNullable: true, validators: [Validators.maxLength(1000)] });

  save() {
    const note = this.noteCtrl.value.trim();
    this.ref.close(note.length > 0 ? note : null);
  }
}
