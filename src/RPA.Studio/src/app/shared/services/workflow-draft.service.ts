import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map } from 'rxjs';
import { WorkflowVersion } from '../models/workflow.model';

interface WorkflowDraftDto {
  id: string;
  workflowId: string;
  version: string;
  jsonDefinition: string;
}

/**
 * Hand-off point for "create workflow from template" (Faz 5, Task 5.5) ve
 * taslak kalıcılığı (Paket B): backend'deki draft'ı yükle/kaydet.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowDraftService {
  private readonly http = inject(HttpClient);
  private readonly _pending = signal<WorkflowVersion | null>(null);

  setPending(workflow: WorkflowVersion): void {
    this._pending.set(workflow);
  }

  /** Returns and clears the pending draft, if any. */
  consumePending(): WorkflowVersion | null {
    const pending = this._pending();
    this._pending.set(null);
    return pending;
  }

  /** Backend'deki taslağı yükler (JsonDefinition parse edilir). */
  load(workflowId: string): Observable<WorkflowVersion> {
    return this.http
      .get<WorkflowDraftDto>(`/api/workflows/${encodeURIComponent(workflowId)}/draft`)
      .pipe(map((dto) => JSON.parse(dto.jsonDefinition) as WorkflowVersion));
  }

  /** Canvas'tan serialize edilen grafiği taslağa kaydeder. */
  save(workflowId: string, version: WorkflowVersion): Observable<void> {
    return this.http
      .put<WorkflowDraftDto>(`/api/workflows/${encodeURIComponent(workflowId)}/draft`, {
        jsonDefinition: JSON.stringify(version),
      })
      .pipe(map(() => undefined));
  }
}
