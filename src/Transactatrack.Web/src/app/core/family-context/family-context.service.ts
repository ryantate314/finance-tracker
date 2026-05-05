import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'transactatrack.activeFamilyId';
const storage = typeof window !== 'undefined' ? window.localStorage : null;

@Injectable({ providedIn: 'root' })
export class FamilyContextService {
  readonly activeFamilyId = signal<string | null>(
    storage?.getItem(STORAGE_KEY) ?? null
  );

  setActive(id: string): void {
    this.activeFamilyId.set(id);
    storage?.setItem(STORAGE_KEY, id);
  }
}
