import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { InstallationRequestDocument, LicenseStatus, SignedLicenseDocument } from './license.models';

/**
 * Offline lisans yonetimi API sarmalayicisi (Task 7).
 * Uclar: GET /api/license/status, GET /api/license/installation-request, POST /api/license/import.
 * Yetkilendirme mevcut auth interceptor'i uzerinden gelir; hicbir lisans/gizli veri
 * tarayici depolamasina (local/sessionStorage) yazilmaz.
 */
@Injectable({ providedIn: 'root' })
export class LicenseService {
  private readonly http = inject(HttpClient);

  getStatus(): Observable<LicenseStatus> {
    return this.http.get<LicenseStatus>('/api/license/status');
  }

  getInstallationRequest(): Observable<InstallationRequestDocument> {
    return this.http.get<InstallationRequestDocument>('/api/license/installation-request');
  }

  import(document: SignedLicenseDocument): Observable<LicenseStatus> {
    return this.http.post<LicenseStatus>('/api/license/import', document);
  }
}

/** API hata govdesinden ({ error }) kararli bir mesaj cikarir. */
export function apiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (typeof body === 'string' && body.trim()) return body;
    if (body && typeof body === 'object' && typeof (body as { error?: unknown }).error === 'string') {
      return (body as { error: string }).error;
    }
  }
  return fallback;
}
