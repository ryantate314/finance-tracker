import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export type RuleMatchField = 'Description' | 'Merchant' | 'AmountRange';
export type RuleMatchType = 'Contains' | 'Equals' | 'Regex';
export type RuleScope = 'Family' | 'Account';

export interface CategoryRuleDto {
  id: string;
  priority: number;
  matchField: RuleMatchField;
  matchType: RuleMatchType;
  pattern: string;
  amountMin: number | null;
  amountMax: number | null;
  targetCategoryId: string;
  targetSubCategoryId: string | null;
  scope: RuleScope;
  accountId: string | null;
  isEnabled: boolean;
}

export interface SaveCategoryRuleRequest {
  priority: number;
  matchField: RuleMatchField;
  matchType: RuleMatchType;
  pattern: string;
  amountMin: number | null;
  amountMax: number | null;
  targetCategoryId: string;
  targetSubCategoryId: string | null;
  scope: RuleScope;
  accountId: string | null;
  isEnabled: boolean;
}

export interface RuleOrderUpdate {
  id: string;
  priority: number;
}

@Injectable({ providedIn: 'root' })
export class CategoryRulesService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/category-rules`;

  list() { return this.http.get<CategoryRuleDto[]>(this.base); }
  get(id: string) { return this.http.get<CategoryRuleDto>(`${this.base}/${id}`); }
  create(req: SaveCategoryRuleRequest) { return this.http.post<CategoryRuleDto>(this.base, req); }
  update(id: string, req: SaveCategoryRuleRequest) { return this.http.put<void>(`${this.base}/${id}`, req); }
  delete(id: string) { return this.http.delete<void>(`${this.base}/${id}`); }
  reorder(updates: RuleOrderUpdate[]) { return this.http.put<void>(`${this.base}/order`, updates); }
}
