import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { TransactionDto } from '../ledger/ledger.service';

@Injectable({ providedIn: 'root' })
export class TransactionsService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/transactions`;

  // Note travels on the same PATCH as the category. Callers doing a category-only edit must
  // pass the transaction's current note so it isn't wiped server-side.
  updateCategory(
    id: string,
    categoryId: string | null,
    subCategoryId: string | null = null,
    note: string | null = null,
  ) {
    return this.http.patch<TransactionDto>(`${this.base}/${id}`, { categoryId, subCategoryId, note });
  }
}
