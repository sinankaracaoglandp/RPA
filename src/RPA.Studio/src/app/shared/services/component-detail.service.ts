import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ComponentVersion, PublishComponentRequest } from '../models/component.model';

/**
 * Wrapper for component library API endpoints (Faz 5, Task 5.3).
 * Backend: GET /api/components, POST /api/components/{componentId}/publish, etc.
 */
@Injectable({ providedIn: 'root' })
export class ComponentDetailService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/components';

  /** Fetch all published components from the library. */
  getComponents(): Observable<ComponentVersion[]> {
    return this.http.get<ComponentVersion[]>(this.baseUrl);
  }

  /** Fetch a specific component version by componentId and version. */
  getComponentVersion(componentId: string, version: string): Observable<ComponentVersion> {
    return this.http.get<ComponentVersion>(`${this.baseUrl}/${encodeURIComponent(componentId)}/${encodeURIComponent(version)}`);
  }

  /**
   * Publish a component (Draft → Published). Requires Developer role.
   * Backend: POST /api/components/{componentId}/publish
   */
  publishComponent(componentId: string, request: PublishComponentRequest): Observable<ComponentVersion> {
    return this.http.post<ComponentVersion>(`${this.baseUrl}/${encodeURIComponent(componentId)}/publish`, request);
  }

  /**
   * Approve a component version (Published). Requires Approver role.
   * Backend: POST /api/components/{componentId}/{version}/approve
   */
  approveComponent(componentId: string, version: string): Observable<ComponentVersion> {
    return this.http.post<ComponentVersion>(
      `${this.baseUrl}/${encodeURIComponent(componentId)}/${encodeURIComponent(version)}/approve`,
      {},
    );
  }
}
