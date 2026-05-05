import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface ApiHealth { status: string; version: string; }
interface DatabaseHealth { status: string; message?: string | null; }
interface OllamaHealth { status: string; models: string[]; message?: string | null; }
interface HealthResponse { api: ApiHealth; database: DatabaseHealth; ollama: OllamaHealth; }

@Component({
  selector: 'app-system-status',
  templateUrl: './system-status.html',
  styleUrl: './system-status.scss',
})
export class SystemStatus implements OnInit {
  private http = inject(HttpClient);

  loading = signal(true);
  error = signal<string | null>(null);
  health = signal<HealthResponse | null>(null);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    this.http.get<HealthResponse>(`${environment.apiBaseUrl}/status`).subscribe({
      next: (h) => {
        this.health.set(h);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.message ?? 'Unable to reach API');
        this.loading.set(false);
      },
    });
  }
}
