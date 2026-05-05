import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export type AccountType = 'Checking' | 'Savings' | 'CreditCard' | 'Loan' | 'Investment' | 'Cash' | 'Other';

export const ACCOUNT_TYPES: AccountType[] = ['Checking', 'Savings', 'CreditCard', 'Loan', 'Investment', 'Cash', 'Other'];

export interface AccountDto {
  id: string;
  familyId: string;
  ownerId: string;
  name: string;
  institution: string | null;
  accountType: AccountType;
  bankCode: string | null;
  createdUtc: string;
}

export interface CreateAccountRequest {
  ownerId: string;
  name: string;
  institution: string | null;
  accountType: AccountType;
  bankCode: string | null;
}

@Injectable({ providedIn: 'root' })
export class AccountsService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/accounts`;

  list() { return this.http.get<AccountDto[]>(this.base); }
  create(req: CreateAccountRequest) { return this.http.post<AccountDto>(this.base, req); }
  update(id: string, req: CreateAccountRequest) { return this.http.put<void>(`${this.base}/${id}`, req); }
  delete(id: string) { return this.http.delete<void>(`${this.base}/${id}`); }
}
