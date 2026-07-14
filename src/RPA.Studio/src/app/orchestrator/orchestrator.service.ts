import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ActionItem,
  AlertRule,
  CreateAlertRuleRequest,
  CreateTriggerRequest,
  CredentialReference,
  DashboardSummary,
  Environment,
  JobRun,
  JobRunListResponse,
  JobRunQuery,
  QueueItemListResponse,
  QueueItem,
  QueueSummary,
  Robot,
  StoreCredentialRequest,
  TriggerDefinition,
  TriggerListQuery,
  UpdateTriggerRequest,
  WorkflowVersion,
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

  /** Alarm kuralları (tümü). */
  listAlertRules(): Observable<AlertRule[]> {
    return this.http.get<AlertRule[]>('/api/alert-rules');
  }

  /** Yeni alarm kuralı oluşturur. */
  createAlertRule(request: CreateAlertRuleRequest): Observable<AlertRule> {
    return this.http.post<AlertRule>('/api/alert-rules', request);
  }

  /** Kuralın aktif/pasif durumunu değiştirir. */
  setAlertRuleActive(id: string, isActive: boolean): Observable<AlertRule> {
    return this.http.patch<AlertRule>(
      `/api/alert-rules/${encodeURIComponent(id)}/active`,
      { isActive },
    );
  }

  /** Action Center: bekleyen kayıtlar (opsiyonel type filtresi). */
  listActionItems(type?: string): Observable<ActionItem[]> {
    let params = new HttpParams();
    if (type) params = params.set('type', type);
    return this.http.get<ActionItem[]>('/api/action-center', { params });
  }

  /** Action Center kaydını çözümler (not ile). */
  resolveActionItem(id: string, note: string): Observable<ActionItem> {
    return this.http.post<ActionItem>(
      `/api/action-center/${encodeURIComponent(id)}/resolve`,
      { note },
    );
  }

  /** Action Center kaydını bir kullanıcıya atar. */
  assignActionItem(id: string, userId: string): Observable<ActionItem> {
    return this.http.post<ActionItem>(
      `/api/action-center/${encodeURIComponent(id)}/assign`,
      { userId },
    );
  }

  /** Tüm kuyruklar + durum bazlı sayaçlar. */
  listQueues(): Observable<QueueSummary[]> {
    return this.http.get<QueueSummary[]>('/api/queues');
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

  /** Tek bir kuyruk kaleminin son durumu. */
  getQueueItem(queueId: string, itemId: string): Observable<QueueItem> {
    return this.http.get<QueueItem>(
      `/api/queues/${encodeURIComponent(queueId)}/items/${encodeURIComponent(itemId)}`,
    );
  }

  // --- WP-6.4: Ortam yönetimi + deployment governance ---

  /** Tüm ortamlar (Dev/Test/Prod). */
  listEnvironments(): Observable<Environment[]> {
    return this.http.get<Environment[]>('/api/environments');
  }

  /** Yeni ortam oluşturur. */
  createEnvironment(name: string, description?: string): Observable<Environment> {
    return this.http.post<Environment>('/api/environments', { name, description });
  }

  /** Bir workflow'un versiyonları (deployment durumu ile). */
  listWorkflowVersions(workflowId: string): Observable<WorkflowVersion[]> {
    return this.http.get<WorkflowVersion[]>(
      `/api/workflows/${encodeURIComponent(workflowId)}/versions`,
    );
  }

  /** Versiyonu Test ortamına yayınlar (Developer). */
  publishWorkflowVersion(workflowId: string, version: string): Observable<WorkflowVersion> {
    return this.http.post<WorkflowVersion>(
      `/api/workflows/${encodeURIComponent(workflowId)}/versions/${encodeURIComponent(version)}/publish`,
      {},
    );
  }

  /** Versiyonu onaylayıp Prod'a terfi ettirir (Approver). */
  approveWorkflowVersion(workflowId: string, version: string): Observable<WorkflowVersion> {
    return this.http.post<WorkflowVersion>(
      `/api/workflows/${encodeURIComponent(workflowId)}/versions/${encodeURIComponent(version)}/approve`,
      {},
    );
  }

  /** Credential Vault referanslarini plaintext secret donmeden listeler. */
  listCredentials(tag?: string): Observable<CredentialReference[]> {
    let params = new HttpParams();
    if (tag) params = params.set('tag', tag);
    return this.http.get<CredentialReference[]>('/api/credentials', { params });
  }

  /** Secret degerini Vault'a yazar; response secret icermez. */
  storeCredential(request: StoreCredentialRequest): Observable<CredentialReference> {
    return this.http.post<CredentialReference>('/api/credentials', request);
  }

  /** Credential referansini Vault'tan siler. */
  deleteCredential(key: string): Observable<void> {
    return this.http.delete<void>(`/api/credentials/${encodeURIComponent(key)}`);
  }

  // --- WP-6.6: Job tanımları (trigger) yönetimi ---

  /** Tüm job tanımları (trigger) — opsiyonel filtrelerle. */
  listTriggers(query: TriggerListQuery = {}): Observable<TriggerDefinition[]> {
    let params = new HttpParams();
    if (query.projectId) params = params.set('projectId', query.projectId);
    if (query.environmentId) params = params.set('environmentId', query.environmentId);
    if (query.isActive != null) params = params.set('isActive', String(query.isActive));
    return this.http.get<TriggerDefinition[]>('/api/triggers', { params });
  }

  /** Yeni job tanımı oluşturur. */
  createTrigger(request: CreateTriggerRequest): Observable<TriggerDefinition> {
    return this.http.post<TriggerDefinition>('/api/triggers', request);
  }

  /** Job tanımını günceller (aktif/pasif, hedef tag, öncelik, schedule). */
  updateTrigger(id: string, request: UpdateTriggerRequest): Observable<TriggerDefinition> {
    return this.http.patch<TriggerDefinition>(`/api/triggers/${encodeURIComponent(id)}`, request);
  }

  /** Job'ı manuel çalıştırır. */
  fireTrigger(id: string): Observable<unknown> {
    return this.http.post(`/api/triggers/${encodeURIComponent(id)}/fire`, {});
  }
}
