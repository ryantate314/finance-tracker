import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface OwnerDto {
  id: string;
  familyId: string;
  name: string;
  createdUtc: string;
}

@Injectable({ providedIn: 'root' })
export class OwnersService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/owners`;

  list() { return this.http.get<OwnerDto[]>(this.base); }
  create(name: string) { return this.http.post<OwnerDto>(this.base, { name }); }
  update(id: string, name: string) { return this.http.put<void>(`${this.base}/${id}`, { name }); }
  delete(id: string) { return this.http.delete<void>(`${this.base}/${id}`); }
}
