import { Component, computed, effect, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { CategoryDto } from './categories.service';

export interface CategorySelection {
  categoryId: string | null;
  subCategoryId: string | null;
}

interface PickerOption {
  key: string;
  categoryId: string | null;
  subCategoryId: string | null;
  display: string;
  primary: string;
  secondary?: string;
  search: string;
  indented: boolean;
}

@Component({
  selector: 'app-category-picker',
  standalone: true,
  imports: [ReactiveFormsModule, MatAutocompleteModule],
  template: `
    <input
      type="text"
      class="picker-input"
      [formControl]="ctrl"
      [matAutocomplete]="auto"
      placeholder="— Uncategorized —"
      (focus)="onFocus($event)"
      (blur)="onBlur()"
    />
    <mat-autocomplete #auto="matAutocomplete"
      panelClass="category-picker-panel"
      [displayWith]="displayFn"
      (optionSelected)="onSelected($event)">
      @for (opt of filtered(); track opt.key) {
        <mat-option [value]="opt" [class.cp-indented]="opt.indented">
          @if (opt.secondary) {
            <span class="cp-muted">{{ opt.secondary }} › </span>
          }
          <span [class.cp-muted]="!opt.categoryId">{{ opt.primary }}</span>
        </mat-option>
      }
    </mat-autocomplete>
  `,
  styles: [`
    :host { display: inline-block; width: 100%; min-width: 220px; }
    .picker-input {
      width: 100%;
      box-sizing: border-box;
      padding: 6px 8px;
      font: inherit;
      font-size: 0.875rem;
      color: inherit;
      background: transparent;
      border: 1px solid transparent;
      border-radius: 4px;
      cursor: pointer;
      outline: none;
      transition: border-color 0.15s ease, background 0.15s ease;
    }
    .picker-input:hover:not(:disabled) { border-color: rgba(0,0,0,0.18); background: rgba(0,0,0,0.02); }
    .picker-input:focus { border-color: rgba(0,0,0,0.4); background: rgba(0,0,0,0.02); cursor: text; }
    .picker-input::placeholder { color: rgba(0,0,0,0.4); }
    .picker-input:disabled { opacity: 0.55; cursor: not-allowed; }
    ::ng-deep .category-picker-panel .mat-mdc-option.cp-indented { padding-left: 32px; }
    ::ng-deep .category-picker-panel .cp-muted { color: rgba(0,0,0,0.55); }
  `],
})
export class CategoryPicker {
  categories = input.required<CategoryDto[]>();
  categoryId = input<string | null>(null);
  subCategoryId = input<string | null>(null);
  disabled = input<boolean>(false);
  selectionChange = output<CategorySelection>();

  ctrl = new FormControl<string | PickerOption | null>(null);

  private rawValue = toSignal(this.ctrl.valueChanges, { initialValue: null });

  options = computed<PickerOption[]>(() => {
    const list: PickerOption[] = [{
      key: 'none',
      categoryId: null,
      subCategoryId: null,
      display: '',
      primary: '— Uncategorized —',
      search: 'uncategorized',
      indented: false,
    }];
    for (const c of this.categories()) {
      list.push({
        key: `c:${c.id}`,
        categoryId: c.id,
        subCategoryId: null,
        display: c.name,
        primary: c.name,
        search: c.name.toLowerCase(),
        indented: false,
      });
      for (const s of c.subCategories ?? []) {
        list.push({
          key: `s:${s.id}`,
          categoryId: c.id,
          subCategoryId: s.id,
          display: `${c.name} › ${s.name}`,
          primary: s.name,
          secondary: c.name,
          search: `${c.name} ${s.name}`.toLowerCase(),
          indented: true,
        });
      }
    }
    return list;
  });

  filtered = computed<PickerOption[]>(() => {
    const v = this.rawValue();
    if (typeof v !== 'string') return this.options();
    const q = v.trim().toLowerCase();
    if (!q) return this.options();
    return this.options().filter(o => o.search.includes(q));
  });

  constructor() {
    effect(() => {
      const opts = this.options();
      const wantCat = this.categoryId();
      const wantSub = this.subCategoryId();
      const found = opts.find(o => o.categoryId === wantCat && o.subCategoryId === wantSub) ?? opts[0];
      this.ctrl.setValue(found, { emitEvent: false });
    });
    effect(() => {
      if (this.disabled()) this.ctrl.disable({ emitEvent: false });
      else this.ctrl.enable({ emitEvent: false });
    });
  }

  displayFn = (opt: PickerOption | string | null): string => {
    if (!opt) return '';
    return typeof opt === 'string' ? opt : opt.display;
  };

  onFocus(e: FocusEvent) {
    (e.target as HTMLInputElement).select();
  }

  onBlur() {
    // If the user typed but never picked an option, ctrl.value is a string. Restore the last good selection.
    const v = this.ctrl.value;
    if (typeof v === 'string') {
      const opts = this.options();
      const found = opts.find(o => o.categoryId === this.categoryId() && o.subCategoryId === this.subCategoryId()) ?? opts[0];
      this.ctrl.setValue(found, { emitEvent: false });
    }
  }

  onSelected(e: MatAutocompleteSelectedEvent) {
    const opt = e.option.value as PickerOption;
    this.selectionChange.emit({ categoryId: opt.categoryId, subCategoryId: opt.subCategoryId });
  }
}
