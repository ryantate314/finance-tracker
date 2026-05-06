import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export type ImportBatchStatus = 'Pending' | 'Committed' | 'Discarded';

export interface ImportBatchDto {
  id: string;
  accountId: string;
  bankCode: string;
  originalFilename: string;
  uploadedUtc: string;
  status: ImportBatchStatus;
  transactionCount: number;
}

export interface ImportPreviewRowDto {
  date: string;
  postedDate: string | null;
  amount: number;
  description: string;
  isDuplicate: boolean;
}

export interface ImportPreviewDto {
  batchId: string;
  accountId: string;
  bankCode: string;
  originalFilename: string;
  uploadedUtc: string;
  totalRows: number;
  newCount: number;
  duplicateCount: number;
  sample: ImportPreviewRowDto[];
}

export interface ImportBatchDetailDto {
  batch: ImportBatchDto;
  transactions: ImportPreviewRowDto[];
}

@Injectable({ providedIn: 'root' })
export class ImportsService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/imports`;

  list() { return this.http.get<ImportBatchDto[]>(this.base); }

  get(id: string) { return this.http.get<ImportBatchDetailDto>(`${this.base}/${id}`); }

  upload(accountId: string, file: File) {
    const form = new FormData();
    form.append('accountId', accountId);
    form.append('file', file, file.name);
    return this.http.post<ImportPreviewDto>(this.base, form);
  }

  commit(id: string) { return this.http.post<void>(`${this.base}/${id}/commit`, null); }
  discard(id: string) { return this.http.post<void>(`${this.base}/${id}/discard`, null); }
}
