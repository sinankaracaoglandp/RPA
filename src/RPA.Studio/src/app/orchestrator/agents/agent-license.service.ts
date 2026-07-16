import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ActivationCodeResponse, AgentDto } from './agent-license.models';

/**
 * Aktive agent yonetimi API sarmalayicisi (Task 8).
 * Uclar: GET/POST /api/agents, POST /api/agents/{id}/activation-code|disable|deactivate.
 * Yetkilendirme mevcut auth interceptor'i uzerinden gelir; hicbir gizli veri
 * tarayici depolamasina (local/sessionStorage) yazilmaz.
 */
@Injectable({ providedIn: 'root' })
export class AgentLicenseService {
  private readonly http = inject(HttpClient);

  list(): Observable<AgentDto[]> {
    return this.http.get<AgentDto[]>('/api/agents');
  }

  create(name: string): Observable<AgentDto> {
    return this.http.post<AgentDto>('/api/agents', { name });
  }

  /** Tek kullanimlik aktivasyon kodu uretir (15 dk gecerli). Yanit yalnizca bellekte tutulur. */
  createActivationCode(id: string): Observable<ActivationCodeResponse> {
    return this.http.post<ActivationCodeResponse>(`/api/agents/${id}/activation-code`, {});
  }

  /** Agent'i devre disi birakir — koltuk KORUNUR. */
  disable(id: string): Observable<void> {
    return this.http.post<void>(`/api/agents/${id}/disable`, {});
  }

  /** Agent aktivasyonunu geri alir — koltuk SERBEST birakilir. */
  deactivate(id: string): Observable<void> {
    return this.http.post<void>(`/api/agents/${id}/deactivate`, {});
  }
}
