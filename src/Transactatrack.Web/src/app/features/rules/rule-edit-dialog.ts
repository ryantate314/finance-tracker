import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatRadioModule } from '@angular/material/radio';
import { AccountsService } from '../accounts/accounts.service';
import { CategoriesService } from '../categories/categories.service';
import { CategoryRuleDto, RuleMatchField, RuleMatchType, RuleScope, SaveCategoryRuleRequest } from './category-rules.service';

export interface RuleEditDialogData {
  rule?: CategoryRuleDto;
}

@Component({
  selector: 'app-rule-edit-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatButtonModule,
    MatSlideToggleModule, MatRadioModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.rule ? 'Edit Rule' : 'New Rule' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid">
        <mat-form-field appearance="outline">
          <mat-label>Priority</mat-label>
          <input matInput type="number" formControlName="priority" />
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Match Field</mat-label>
          <mat-select formControlName="matchField">
            <mat-option value="Description">Description</mat-option>
            <mat-option value="Merchant">Merchant</mat-option>
            <mat-option value="AmountRange">Amount Range</mat-option>
          </mat-select>
        </mat-form-field>

        @if (isTextMatch()) {
          <mat-form-field appearance="outline">
            <mat-label>Match Type</mat-label>
            <mat-select formControlName="matchType">
              <mat-option value="Contains">Contains</mat-option>
              <mat-option value="Equals">Equals</mat-option>
              <mat-option value="Regex">Regex</mat-option>
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Pattern</mat-label>
            <input matInput formControlName="pattern" />
          </mat-form-field>
        } @else {
          <mat-form-field appearance="outline">
            <mat-label>Min Amount (absolute)</mat-label>
            <input matInput type="number" formControlName="amountMin" placeholder="optional" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Max Amount (absolute)</mat-label>
            <input matInput type="number" formControlName="amountMax" placeholder="optional" />
          </mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>Target Category</mat-label>
          <mat-select formControlName="targetCategoryId">
            @for (c of categories(); track c.id) {
              <mat-option [value]="c.id">{{ c.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        @if (subCategoriesForSelected().length > 0) {
          <mat-form-field appearance="outline">
            <mat-label>Target Sub-Category</mat-label>
            <mat-select formControlName="targetSubCategoryId">
              <mat-option [value]="null">— none —</mat-option>
              @for (s of subCategoriesForSelected(); track s.id) {
                <mat-option [value]="s.id">{{ s.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }

        <div class="scope-row">
          <label>Scope:</label>
          <mat-radio-group formControlName="scope" class="radio-group">
            <mat-radio-button value="Family">Family</mat-radio-button>
            <mat-radio-button value="Account">Account</mat-radio-button>
          </mat-radio-group>
        </div>

        @if (isAccountScope()) {
          <mat-form-field appearance="outline">
            <mat-label>Account</mat-label>
            <mat-select formControlName="accountId">
              @for (a of accounts(); track a.id) {
                <mat-option [value]="a.id">{{ a.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }

        <mat-slide-toggle formControlName="isEnabled" class="toggle">Enabled</mat-slide-toggle>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="form.invalid" (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .form-grid { display: flex; flex-direction: column; gap: 4px; min-width: 380px; }
    mat-form-field { width: 100%; }
    mat-dialog-content { padding-top: 8px; overflow: visible; }
    .scope-row { display: flex; align-items: center; gap: 12px; padding: 4px 0; }
    .radio-group { display: flex; gap: 16px; }
    .toggle { margin: 8px 0; }
  `],
})
export class RuleEditDialog {
  data = inject<RuleEditDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<RuleEditDialog, SaveCategoryRuleRequest>);
  private categoriesSvc = inject(CategoriesService);
  private accountsSvc = inject(AccountsService);

  categories = toSignal(this.categoriesSvc.list(), { initialValue: [] });
  accounts = toSignal(this.accountsSvc.list(), { initialValue: [] });

  form = new FormGroup({
    priority: new FormControl(this.data.rule?.priority ?? 10, [Validators.required, Validators.min(0)]),
    matchField: new FormControl<RuleMatchField>(this.data.rule?.matchField ?? 'Description', Validators.required),
    matchType: new FormControl<RuleMatchType>(this.data.rule?.matchType ?? 'Contains', Validators.required),
    pattern: new FormControl(this.data.rule?.pattern ?? ''),
    amountMin: new FormControl<number | null>(this.data.rule?.amountMin ?? null),
    amountMax: new FormControl<number | null>(this.data.rule?.amountMax ?? null),
    targetCategoryId: new FormControl(this.data.rule?.targetCategoryId ?? '', Validators.required),
    targetSubCategoryId: new FormControl<string | null>(this.data.rule?.targetSubCategoryId ?? null),
    scope: new FormControl<RuleScope>(this.data.rule?.scope ?? 'Family', Validators.required),
    accountId: new FormControl<string | null>(this.data.rule?.accountId ?? null),
    isEnabled: new FormControl(this.data.rule?.isEnabled ?? true),
  });

  isTextMatch = toSignal(
    this.form.controls.matchField.valueChanges,
    { initialValue: this.data.rule?.matchField ?? 'Description' }
  );
  isAccountScope = toSignal(
    this.form.controls.scope.valueChanges,
    { initialValue: this.data.rule?.scope ?? 'Family' }
  );

  private targetCategoryId = toSignal(
    this.form.controls.targetCategoryId.valueChanges,
    { initialValue: this.data.rule?.targetCategoryId ?? '' }
  );

  subCategoriesForSelected = computed(() => {
    const id = this.targetCategoryId();
    if (!id) return [];
    return this.categories().find(c => c.id === id)?.subCategories ?? [];
  });

  constructor() {
    // Clear sub-category when its parent category changes away from it.
    effect(() => {
      const cur = this.form.controls.targetSubCategoryId.value;
      if (!cur) return;
      const valid = this.subCategoriesForSelected().some(s => s.id === cur);
      if (!valid) this.form.controls.targetSubCategoryId.setValue(null);
    });
  }

  get isTextMatchVal() { return this.form.controls.matchField.value !== 'AmountRange'; }
  get isAccountScopeVal() { return this.form.controls.scope.value === 'Account'; }

  save() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.ref.close({
      priority: v.priority!,
      matchField: v.matchField!,
      matchType: v.matchType!,
      pattern: v.matchField === 'AmountRange' ? '' : (v.pattern ?? ''),
      amountMin: v.matchField === 'AmountRange' ? v.amountMin : null,
      amountMax: v.matchField === 'AmountRange' ? v.amountMax : null,
      targetCategoryId: v.targetCategoryId!,
      targetSubCategoryId: v.targetSubCategoryId ?? null,
      scope: v.scope!,
      accountId: v.scope === 'Account' ? v.accountId : null,
      isEnabled: v.isEnabled ?? true,
    });
  }
}
