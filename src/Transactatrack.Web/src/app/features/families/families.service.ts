import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface FamilyDto {
  id: string;
  name: string;
  createdUtc: string;
}

@Injectable({ providedIn: 'root' })
export class FamiliesService {
  private http = inject(HttpClient);
  private base = `${environment.apiBaseUrl}/families`;

  private _families$ = new BehaviorSubject<FamilyDto[]>([]);
  readonly families$ = this._families$.asObservable();

  refresh() {
    return this.http.get<FamilyDto[]>(this.base).pipe(
      tap(f => this._families$.next(f))
    );
  }

  get(id: string) { return this.http.get<FamilyDto>(`${this.base}/${id}`); }
  create(name: string) { return this.http.post<FamilyDto>(this.base, { name }); }
  update(id: string, name: string) { return this.http.put<void>(`${this.base}/${id}`, { name }); }
  delete(id: string) { return this.http.delete<void>(`${this.base}/${id}`); }
}
