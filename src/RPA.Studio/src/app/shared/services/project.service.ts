import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface ProjectSummary {
  id: string;
  name: string;
  description?: string;
  workflowCount: number;
}

export interface WorkflowSummary {
  id: string;
  name: string;
  updatedAt?: string;
}

/** Projelerim ekranının backend erişimi (Paket B — /api/projects). */
@Injectable({ providedIn: 'root' })
export class ProjectService {
  private readonly http = inject(HttpClient);

  getProjects(): Observable<ProjectSummary[]> {
    return this.http.get<ProjectSummary[]>('/api/projects');
  }

  createProject(name: string, description?: string): Observable<ProjectSummary> {
    return this.http.post<ProjectSummary>('/api/projects', { name, description });
  }

  getWorkflows(projectId: string): Observable<WorkflowSummary[]> {
    return this.http.get<WorkflowSummary[]>(
      `/api/projects/${encodeURIComponent(projectId)}/workflows`,
    );
  }

  createWorkflow(projectId: string, name: string): Observable<WorkflowSummary> {
    return this.http.post<WorkflowSummary>(
      `/api/projects/${encodeURIComponent(projectId)}/workflows`,
      { name },
    );
  }
}
