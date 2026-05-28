import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamiliesService, FamilyImportSummaryDto } from './families.service';

type Mode = 'new' | 'merge';

@Component({
  selector: 'app-family-import-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <h2 mat-dialog-title>Import Family</h2>
    <mat-dialog-content>
      @if (summary(); as s) {
        <div class="summary">
          <p>Imported into <strong>{{ s.familyName }}</strong>.</p>
          <table>
            <tr><th></th><th>Inserted</th><th>Skipped</th></tr>
            <tr><td>Owners</td><td>{{ s.ownersInserted }}</td><td>{{ s.ownersSkipped }}</td></tr>
            <tr><td>Accounts</td><td>{{ s.accountsInserted }}</td><td>{{ s.accountsSkipped }}</td></tr>
            <tr>
              <td>Categories</td>
              <td>{{ s.categoriesInserted }}</td>
              <td>{{ s.categoriesSkipped }}@if (s.categoriesRemapped) { ({{ s.categoriesRemapped }} remapped) }</td>
            </tr>
            <tr><td>Sub-categories</td><td>{{ s.subCategoriesInserted }}</td><td>{{ s.subCategoriesSkipped }}</td></tr>
            <tr><td>Rules</td><td>{{ s.categoryRulesInserted }}</td><td>{{ s.categoryRulesSkipped }}</td></tr>
            <tr><td>Import batches</td><td>{{ s.importBatchesInserted }}</td><td>{{ s.importBatchesSkipped }}</td></tr>
            <tr><td>Transactions</td><td>{{ s.transactionsInserted }}</td><td>{{ s.transactionsSkipped }}</td></tr>
          </table>
        </div>
      } @else {
        <form [formGroup]="form">
          <div class="file-row">
            <button mat-stroked-button type="button" (click)="picker.click()">Choose JSON file</button>
            <input #picker type="file" accept="application/json,.json" hidden (change)="onFile($event)" />
            <span class="file-name">{{ file()?.name ?? 'No file selected' }}</span>
          </div>

          <mat-radio-group formControlName="mode" class="mode-group">
            <mat-radio-button value="new">Import as new family</mat-radio-button>
            <mat-radio-button value="merge">Merge into existing family</mat-radio-button>
          </mat-radio-group>

          @if (form.value.mode === 'new') {
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Name (optional override)</mat-label>
              <input matInput formControlName="name" />
            </mat-form-field>
          } @else {
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Target family</mat-label>
              <mat-select formControlName="targetFamilyId">
                @for (f of families() ?? []; track f.id) {
                  <mat-option [value]="f.id">{{ f.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }

          @if (busy()) {
            <div class="busy"><mat-spinner diameter="24"></mat-spinner><span>Importing…</span></div>
          }
        </form>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (summary()) {
        <button mat-flat-button (click)="close(true)">Close</button>
      } @else {
        <button mat-button (click)="close(false)" [disabled]="busy()">Cancel</button>
        <button mat-flat-button (click)="submit()" [disabled]="!canSubmit()">Import</button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; }
    mat-dialog-content { padding-top: 8px; overflow: visible; min-width: 420px; }
    .file-row { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
    .file-name { color: rgba(0,0,0,0.6); font-size: 0.9em; }
    .mode-group { display: flex; flex-direction: column; gap: 8px; margin-bottom: 16px; }
    .busy { display: flex; align-items: center; gap: 8px; color: rgba(0,0,0,0.6); }
    .summary table { border-collapse: collapse; width: 100%; margin-top: 8px; }
    .summary th, .summary td { text-align: left; padding: 4px 12px 4px 0; }
    .summary th { font-weight: 600; }
    .summary td:nth-child(2), .summary td:nth-child(3) { text-align: right; font-variant-numeric: tabular-nums; }
  `],
})
export class FamilyImportDialog {
  private svc = inject(FamiliesService);
  private ref = inject(MatDialogRef<FamilyImportDialog, boolean>);
  private snack = inject(MatSnackBar);

  file = signal<File | null>(null);
  busy = signal(false);
  summary = signal<FamilyImportSummaryDto | null>(null);
  families = toSignal(this.svc.families$);

  form = new FormGroup({
    mode: new FormControl<Mode>('new', { nonNullable: true, validators: [Validators.required] }),
    name: new FormControl<string>(''),
    targetFamilyId: new FormControl<string>(''),
  });

  onFile(ev: Event) {
    const input = ev.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
  }

  canSubmit(): boolean {
    if (this.busy() || !this.file()) return false;
    const v = this.form.value;
    if (v.mode === 'merge' && !v.targetFamilyId) return false;
    return true;
  }

  submit() {
    const file = this.file();
    if (!file) return;
    const v = this.form.value;
    this.busy.set(true);

    const obs = v.mode === 'new'
      ? this.svc.importAsNew(file, v.name?.trim() || undefined)
      : this.svc.mergeInto(v.targetFamilyId!, file);

    obs.subscribe({
      next: (s) => { this.summary.set(s); this.busy.set(false); },
      error: (e) => {
        this.busy.set(false);
        this.snack.open(extractErrorMessage(e), 'Close', { duration: 6000 });
      },
    });
  }

  close(didImport: boolean) {
    this.ref.close(didImport || this.summary() !== null);
  }
}
