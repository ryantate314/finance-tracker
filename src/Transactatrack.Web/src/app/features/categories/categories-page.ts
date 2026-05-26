import { Component, effect, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { extractErrorMessage } from '../../core/api/api-error';
import { FamilyContextService } from '../../core/family-context/family-context.service';
import { CategoriesService, CategoryDto, SubCategoryDto } from './categories.service';

@Component({
  selector: 'app-categories-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatExpansionModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  template: `
    <div class="page-header">
      <h2>Categories</h2>
    </div>

    <mat-accordion multi>
      @for (cat of categories(); track cat.id) {
        <mat-expansion-panel>
          <mat-expansion-panel-header>
            <mat-panel-title class="panel-title">
              @if (editingCategoryId() === cat.id) {
                <mat-form-field appearance="outline" class="inline-field" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()">
                  <input matInput [formControl]="editCategoryCtrl" (keydown.enter)="saveCategoryEdit(cat)" (keydown.escape)="cancelEdit()" />
                </mat-form-field>
                <button mat-icon-button aria-label="Save" (click)="saveCategoryEdit(cat); $event.stopPropagation()"><mat-icon>check</mat-icon></button>
                <button mat-icon-button aria-label="Cancel" (click)="cancelEdit(); $event.stopPropagation()"><mat-icon>close</mat-icon></button>
              } @else {
                <span class="category-name">{{ cat.name }}</span>
                @if (cat.kind !== 'User') {
                  <mat-icon class="system-badge" aria-label="System category" title="System category">lock</mat-icon>
                } @else {
                  <button mat-icon-button aria-label="Edit category" (click)="startCategoryEdit(cat); $event.stopPropagation()"><mat-icon>edit</mat-icon></button>
                  <button mat-icon-button aria-label="Delete category" (click)="deleteCategory(cat); $event.stopPropagation()"><mat-icon>delete</mat-icon></button>
                }
              }
            </mat-panel-title>
          </mat-expansion-panel-header>

          <!-- Sub-categories -->
          <div class="sub-list">
            @for (sub of cat.subCategories; track sub.id) {
              <div class="sub-row">
                @if (editingSubId() === sub.id) {
                  <mat-form-field appearance="outline" class="inline-field">
                    <input matInput [formControl]="editSubCtrl" (keydown.enter)="saveSubEdit(cat, sub)" (keydown.escape)="cancelEdit()" />
                  </mat-form-field>
                  <button mat-icon-button aria-label="Save" (click)="saveSubEdit(cat, sub)"><mat-icon>check</mat-icon></button>
                  <button mat-icon-button aria-label="Cancel" (click)="cancelEdit()"><mat-icon>close</mat-icon></button>
                } @else {
                  <span class="sub-name">{{ sub.name }}</span>
                  <button mat-icon-button aria-label="Edit sub-category" (click)="startSubEdit(sub)"><mat-icon>edit</mat-icon></button>
                  <button mat-icon-button aria-label="Delete sub-category" (click)="deleteSub(cat, sub)"><mat-icon>delete</mat-icon></button>
                }
              </div>
            }

            <!-- Inline add sub-category -->
            @if (addingSubForCategoryId() === cat.id) {
              <div class="sub-row add-row">
                <mat-form-field appearance="outline" class="inline-field">
                  <mat-label>New sub-category</mat-label>
                  <input matInput [formControl]="newSubCtrl" (keydown.enter)="saveNewSub(cat)" (keydown.escape)="cancelAdd()" />
                </mat-form-field>
                <button mat-icon-button aria-label="Save" (click)="saveNewSub(cat)"><mat-icon>check</mat-icon></button>
                <button mat-icon-button aria-label="Cancel" (click)="cancelAdd()"><mat-icon>close</mat-icon></button>
              </div>
            } @else {
              <button mat-button (click)="startAddSub(cat)">
                <mat-icon>add</mat-icon> Add sub-category
              </button>
            }
          </div>
        </mat-expansion-panel>
      }
    </mat-accordion>

    <!-- Add new category -->
    <div class="new-category-row">
      @if (addingCategory()) {
        <mat-form-field appearance="outline" class="inline-field">
          <mat-label>New category</mat-label>
          <input matInput [formControl]="newCategoryCtrl" (keydown.enter)="saveNewCategory()" (keydown.escape)="cancelAdd()" />
        </mat-form-field>
        <button mat-icon-button aria-label="Save" (click)="saveNewCategory()"><mat-icon>check</mat-icon></button>
        <button mat-icon-button aria-label="Cancel" (click)="cancelAdd()"><mat-icon>close</mat-icon></button>
      } @else {
        <button mat-flat-button (click)="startAddCategory()">
          <mat-icon>add</mat-icon> New Category
        </button>
      }
    </div>
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: center; padding: 16px 0; }
    .panel-title { display: flex; align-items: center; gap: 4px; width: 100%; }
    .category-name { flex: 1; font-weight: 500; }
    .sub-list { display: flex; flex-direction: column; gap: 4px; padding: 4px 0; }
    .sub-row { display: flex; align-items: center; gap: 4px; padding: 2px 0; }
    .sub-name { flex: 1; padding-left: 8px; }
    .inline-field { flex: 1; }
    .new-category-row { margin-top: 16px; display: flex; align-items: center; gap: 8px; }
    .add-row { padding-top: 8px; }
    .system-badge { color: rgba(0,0,0,0.45); font-size: 18px; width: 18px; height: 18px; margin: 0 8px; }
  `],
})
export class CategoriesPage {
  private svc = inject(CategoriesService);
  private snack = inject(MatSnackBar);
  private familyCtx = inject(FamilyContextService);

  categories = signal<CategoryDto[]>([]);

  editingCategoryId = signal<string | null>(null);
  editCategoryCtrl = new FormControl('', [Validators.required, Validators.maxLength(200)]);

  editingSubId = signal<string | null>(null);
  editSubCtrl = new FormControl('', [Validators.required, Validators.maxLength(200)]);

  addingCategory = signal(false);
  newCategoryCtrl = new FormControl('', [Validators.required, Validators.maxLength(200)]);

  addingSubForCategoryId = signal<string | null>(null);
  newSubCtrl = new FormControl('', [Validators.required, Validators.maxLength(200)]);

  constructor() {
    effect(() => {
      const id = this.familyCtx.activeFamilyId();
      if (!id) return;
      this.load();
    });
  }

  private load() {
    this.svc.list().subscribe(c => this.categories.set(c));
  }

  // ── Category edit ──────────────────────────────────────────────────────────

  startCategoryEdit(cat: CategoryDto) {
    this.cancelAdd();
    this.editingCategoryId.set(cat.id);
    this.editCategoryCtrl.setValue(cat.name);
  }

  saveCategoryEdit(cat: CategoryDto) {
    if (this.editCategoryCtrl.invalid) return;
    this.svc.update(cat.id, this.editCategoryCtrl.value!).subscribe({
      next: () => { this.editingCategoryId.set(null); this.load(); },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  deleteCategory(cat: CategoryDto) {
    this.svc.delete(cat.id).subscribe({
      next: () => this.load(),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  // ── Add category ───────────────────────────────────────────────────────────

  startAddCategory() {
    this.cancelEdit();
    this.addingCategory.set(true);
    this.newCategoryCtrl.reset();
  }

  saveNewCategory() {
    if (this.newCategoryCtrl.invalid) return;
    this.svc.create(this.newCategoryCtrl.value!).subscribe({
      next: () => { this.addingCategory.set(false); this.load(); },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  // ── Sub-category edit ──────────────────────────────────────────────────────

  startSubEdit(sub: SubCategoryDto) {
    this.cancelAdd();
    this.editingSubId.set(sub.id);
    this.editSubCtrl.setValue(sub.name);
  }

  saveSubEdit(cat: CategoryDto, sub: SubCategoryDto) {
    if (this.editSubCtrl.invalid) return;
    this.svc.updateSub(cat.id, sub.id, this.editSubCtrl.value!).subscribe({
      next: () => { this.editingSubId.set(null); this.load(); },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  deleteSub(cat: CategoryDto, sub: SubCategoryDto) {
    this.svc.deleteSub(cat.id, sub.id).subscribe({
      next: () => this.load(),
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  // ── Add sub-category ───────────────────────────────────────────────────────

  startAddSub(cat: CategoryDto) {
    this.cancelEdit();
    this.addingSubForCategoryId.set(cat.id);
    this.newSubCtrl.reset();
  }

  saveNewSub(cat: CategoryDto) {
    if (this.newSubCtrl.invalid) return;
    this.svc.createSub(cat.id, this.newSubCtrl.value!).subscribe({
      next: () => { this.addingSubForCategoryId.set(null); this.load(); },
      error: e => this.snack.open(extractErrorMessage(e), 'Close', { duration: 4000 }),
    });
  }

  // ── Shared cancel ──────────────────────────────────────────────────────────

  cancelEdit() {
    this.editingCategoryId.set(null);
    this.editingSubId.set(null);
  }

  cancelAdd() {
    this.addingCategory.set(false);
    this.addingSubForCategoryId.set(null);
  }
}
