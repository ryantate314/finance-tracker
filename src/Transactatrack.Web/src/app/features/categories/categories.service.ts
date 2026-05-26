import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface SubCategoryDto {
  id: string;
  categoryId: string;
  name: string;
  createdUtc: string;
}

export type CategoryKind = 'User' | 'Transfer' | 'Income';

export interface CategoryDto {
  id: string;
  name: string;
  kind: CategoryKind;
  createdUtc: string;
  subCategories: SubCategoryDto[];
}

@Injectable({ providedIn: 'root' })
export class CategoriesService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/categories`;

  list() { return this.http.get<CategoryDto[]>(this.base); }
  create(name: string) { return this.http.post<CategoryDto>(this.base, { name }); }
  update(id: string, name: string) { return this.http.put<void>(`${this.base}/${id}`, { name }); }
  delete(id: string) { return this.http.delete<void>(`${this.base}/${id}`); }

  createSub(categoryId: string, name: string) {
    return this.http.post<SubCategoryDto>(`${this.base}/${categoryId}/subcategories`, { name });
  }
  updateSub(categoryId: string, id: string, name: string) {
    return this.http.put<void>(`${this.base}/${categoryId}/subcategories/${id}`, { name });
  }
  deleteSub(categoryId: string, id: string) {
    return this.http.delete<void>(`${this.base}/${categoryId}/subcategories/${id}`);
  }
}
