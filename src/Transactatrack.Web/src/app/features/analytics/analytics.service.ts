import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface CategoryBreakdownItem {
  categoryId: string | null;
  categoryName: string;
  amount: number;
  transactionCount: number;
}

export interface MonthlyCashflowItem {
  year: number;
  month: number;
  income: number;
  expense: number;
  net: number;
}

export interface AnalyticsQuery {
  from: Date;
  to: Date;
  accountIds?: string[];
}

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/analytics`;

  categoryBreakdown(q: AnalyticsQuery) {
    return this.http.get<CategoryBreakdownItem[]>(`${this.base}/category-breakdown`, {
      params: this.buildParams(q),
    });
  }

  monthlyCashflow(q: AnalyticsQuery) {
    return this.http.get<MonthlyCashflowItem[]>(`${this.base}/monthly-cashflow`, {
      params: this.buildParams(q),
    });
  }

  private buildParams(q: AnalyticsQuery): HttpParams {
    let params = new HttpParams()
      .set('from', this.dateToYmd(q.from))
      .set('to', this.dateToYmd(q.to));
    if (q.accountIds?.length) params = params.set('accountIds', q.accountIds.join(','));
    return params;
  }

  private dateToYmd(d: Date): string {
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }
}
