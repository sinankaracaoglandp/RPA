import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TemplateMetadata } from '../models/template.model';

/**
 * Wrapper for the workflow template gallery API endpoints (Faz 5, Task 5.5).
 * Backend: GET /api/templates, GET /api/templates/{id}.
 *
 * Contract decision: templates are served from a dedicated /api/templates
 * endpoint rather than reusing the Component Library (/api/components).
 * Templates are full workflow starting points (name/description/icon/category
 * plus a complete workflowJson graph) — a different shape and lifecycle than
 * publishable components (Draft/Published/Deprecated, reusable sub-flows).
 * Reusing /api/components would force template-only fields onto the
 * component model and conflate two unrelated concepts.
 */
@Injectable({ providedIn: 'root' })
export class TemplateService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/templates';

  /** Fetch all available workflow templates. */
  getTemplates(): Observable<TemplateMetadata[]> {
    return this.http.get<TemplateMetadata[]>(this.baseUrl);
  }

  /** Fetch a single template by id (full detail, including workflowJson). */
  getTemplate(id: string): Observable<TemplateMetadata> {
    return this.http.get<TemplateMetadata>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
