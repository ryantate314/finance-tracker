import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { TransactionDto } from '../ledger/ledger.service';

@Injectable({ providedIn: 'root' })
export class TransactionsService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/transactions`;

  // Note and categoryId travel on the same PATCH. Callers doing a single-field edit must
  // echo the other current values so they aren't wiped server-side.
  updateCategory(
    id: string,
    categoryId: string | null,
    subCategoryId: string | null = null,
    note: string | null = null,
    accountId: string | null = null,
  ) {
    return this.http.patch<TransactionDto>(`${this.base}/${id}`, { categoryId, subCategoryId, note, accountId });
  }
}
