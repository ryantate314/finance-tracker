import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface TransferMatchResult {
  paired: number;
  scanned: number;
}

@Injectable({ providedIn: 'root' })
export class TransfersService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/transfers`;

  rescan() {
    return this.http.post<TransferMatchResult>(`${this.base}/rescan`, {});
  }

  link(transactionIdA: string, transactionIdB: string) {
    return this.http.post<{ transferGroupId: string }>(`${this.base}/link`, {
      transactionIdA,
      transactionIdB,
    });
  }

  unlink(groupId: string) {
    return this.http.post<void>(`${this.base}/${groupId}/unlink`, {});
  }
}
