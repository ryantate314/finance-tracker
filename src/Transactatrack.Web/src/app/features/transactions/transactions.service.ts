import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { TransactionDto } from '../ledger/ledger.service';

@Injectable({ providedIn: 'root' })
export class TransactionsService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/transactions`;

  updateCategory(id: string, categoryId: string | null) {
    return this.http.patch<TransactionDto>(`${this.base}/${id}`, { categoryId });
  }
}
