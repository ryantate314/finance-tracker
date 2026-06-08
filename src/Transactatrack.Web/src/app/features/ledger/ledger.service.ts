import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export type CategorizationSource = 'Manual' | 'Rule' | 'Llm';

export interface TransactionDto {
  id: string;
  accountId: string;
  date: string;
  postedDate: string | null;
  amount: number;
  description: string;
  merchant: string | null;
  note: string | null;
  categoryId: string | null;
  subCategoryId: string | null;
  isTransfer: boolean;
  transferGroupId: string | null;
  importBatchId: string;
  createdUtc: string;
  categorizationSource: CategorizationSource;
  needsReview: boolean;
  llmConfidence: number | null;
  appliedRuleId: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface LedgerQuery {
  accountIds?: string[];
  categoryIds?: string[];
  from?: Date | null;
  to?: Date | null;
  q?: string;
  needsReview?: boolean;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class LedgerService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/transactions`;

  list(q: LedgerQuery) {
    let params = new HttpParams();
    if (q.accountIds?.length) params = params.set('accountIds', q.accountIds.join(','));
    if (q.categoryIds?.length) params = params.set('categoryIds', q.categoryIds.join(','));
    if (q.from) params = params.set('from', this.dateToYmd(q.from));
    if (q.to) params = params.set('to', this.dateToYmd(q.to));
    if (q.q?.trim()) params = params.set('q', q.q.trim());
    if (q.needsReview !== undefined) params = params.set('needsReview', String(q.needsReview));
    if (q.page) params = params.set('page', q.page);
    if (q.pageSize) params = params.set('pageSize', q.pageSize);
    return this.http.get<PagedResult<TransactionDto>>(this.base, { params });
  }

  private dateToYmd(d: Date): string {
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }
}
