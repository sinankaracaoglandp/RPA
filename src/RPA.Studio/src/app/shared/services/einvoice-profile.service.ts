import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateEInvoiceProfileRequest,
  EInvoiceProfile,
  EInvoiceProfileVersion,
} from '../models/einvoice-profile.model';

@Injectable({ providedIn: 'root' })
export class EInvoiceProfileService {
  private readonly http = inject(HttpClient);

  list(projectId: string): Observable<EInvoiceProfile[]> {
    return this.http.get<EInvoiceProfile[]>(this.base(projectId));
  }

  create(projectId: string, request: CreateEInvoiceProfileRequest): Observable<EInvoiceProfile> {
    return this.http.post<EInvoiceProfile>(this.base(projectId), request);
  }

  saveDraft(projectId: string, profileId: string, definitionJson: string): Observable<EInvoiceProfile> {
    return this.http.put<EInvoiceProfile>(`${this.base(projectId)}/${encodeURIComponent(profileId)}/draft`, {
      definitionJson,
    });
  }

  publish(projectId: string, profileId: string): Observable<EInvoiceProfileVersion> {
    return this.http.post<EInvoiceProfileVersion>(
      `${this.base(projectId)}/${encodeURIComponent(profileId)}/publish`,
      {},
    );
  }

  versions(projectId: string, profileId: string): Observable<EInvoiceProfileVersion[]> {
    return this.http.get<EInvoiceProfileVersion[]>(
      `${this.base(projectId)}/${encodeURIComponent(profileId)}/versions`,
    );
  }

  private base(projectId: string): string {
    return `/api/projects/${encodeURIComponent(projectId)}/einvoice-profiles`;
  }
}
