import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DashboardSummary,
  JobRun,
  JobRunListResponse,
  JobRunQuery,
  QueueItemListResponse,
  Robot,
} from './orchestrator.models';

/**
 * Orchestrator read-side API sarmalayıcısı (WP-6.1). Backend uçları:
 * GET /api/jobruns, /api/jobruns/{id}, /api/jobruns/dashboard,
 * GET /api/robots, GET /api/queues/{id}/items.
 */
@Injectable({ providedIn: 'root' })
export class OrchestratorService {
  private readonly http = inject(HttpClient);

  /** Dashboard özeti (varsayılan bugün, opsiyonel ortam). */
  getDashboard(environmentId?: string): Observable<DashboardSummary> {
    let params = new HttpParams();
    if (environmentId) {
      params = params.set('environmentId', environmentId);
    }
    return this.http.get<DashboardSummary>('/api/jobruns/dashboard', { params });
  }

  /** İşler listesi (filtreli + sayfalı). */
  listJobs(query: JobRunQuery = {}): Observable<JobRunListResponse> {
    let params = new HttpParams();
    if (query.status) params = params.set('status', query.status);
    if (query.environmentId) params = params.set('environmentId', query.environmentId);
    if (query.robotId) params = params.set('robotId', query.robotId);
    if (query.skip != null) params = params.set('skip', String(query.skip));
    if (query.take != null) params = params.set('take', String(query.take));
    return this.http.get<JobRunListResponse>('/api/jobruns', { params });
  }

  /** İş detayı. */
  getJob(id: string): Observable<JobRun> {
    return this.http.get<JobRun>(`/api/jobruns/${encodeURIComponent(id)}`);
  }

  /** Tüm robotlar. */
  listRobots(): Observable<Robot[]> {
    return this.http.get<Robot[]>('/api/robots');
  }

  /** Bir kuyruğun kalemleri (opsiyonel durum filtresi + sayfalama). */
  listQueueItems(
    queueId: string,
    status?: string,
    skip = 0,
    take = 50,
  ): Observable<QueueItemListResponse> {
    let params = new HttpParams().set('skip', String(skip)).set('take', String(take));
    if (status) params = params.set('status', status);
    return this.http.get<QueueItemListResponse>(
      `/api/queues/${encodeURIComponent(queueId)}/items`,
      { params },
    );
  }
}
