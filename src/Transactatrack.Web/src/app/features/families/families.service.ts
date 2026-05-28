import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface FamilyDto {
  id: string;
  name: string;
  createdUtc: string;
}

export interface FamilyDeleteImpactDto {
  familyId: string;
  familyName: string;
  owners: number;
  accounts: number;
  categories: number;
  subCategories: number;
  categoryRules: number;
  importBatches: number;
  transactions: number;
}

export interface FamilyImportSummaryDto {
  familyId: string;
  familyName: string;
  ownersInserted: number;       ownersSkipped: number;
  accountsInserted: number;     accountsSkipped: number;
  categoriesInserted: number;   categoriesSkipped: number;   categoriesRemapped: number;
  subCategoriesInserted: number; subCategoriesSkipped: number;
  categoryRulesInserted: number; categoryRulesSkipped: number;
  importBatchesInserted: number; importBatchesSkipped: number;
  transactionsInserted: number; transactionsSkipped: number;
}

@Injectable({ providedIn: 'root' })
export class FamiliesService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/families`;

  private _families$ = new BehaviorSubject<FamilyDto[]>([]);
  readonly families$ = this._families$.asObservable();

  refresh() {
    return this.http.get<FamilyDto[]>(this.base).pipe(
      tap(f => this._families$.next(f))
    );
  }

  get(id: string) { return this.http.get<FamilyDto>(`${this.base}/${id}`); }
  create(name: string) { return this.http.post<FamilyDto>(this.base, { name }); }
  update(id: string, name: string) { return this.http.put<void>(`${this.base}/${id}`, { name }); }
  delete(id: string, cascade = false) {
    const url = cascade ? `${this.base}/${id}?cascade=true` : `${this.base}/${id}`;
    return this.http.delete<void>(url);
  }
  getDeleteImpact(id: string) {
    return this.http.get<FamilyDeleteImpactDto>(`${this.base}/${id}/delete-impact`);
  }

  exportFamily(id: string) {
    return this.http.get(`${this.base}/${id}/export`, {
      responseType: 'blob',
      observe: 'response',
    });
  }

  importAsNew(file: Blob, name?: string) {
    const url = name
      ? `${this.base}/import?name=${encodeURIComponent(name)}`
      : `${this.base}/import`;
    return this.http.post<FamilyImportSummaryDto>(url, file, {
      headers: { 'Content-Type': 'application/json' },
    });
  }

  mergeInto(targetFamilyId: string, file: Blob) {
    return this.http.post<FamilyImportSummaryDto>(
      `${this.base}/${targetFamilyId}/import`,
      file,
      { headers: { 'Content-Type': 'application/json' } },
    );
  }
}
