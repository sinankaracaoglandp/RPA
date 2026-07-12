import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse, RefreshRequest } from './auth.models';

const TOKEN_KEY = 'rpa.auth.token';
const REFRESH_TOKEN_KEY = 'rpa.auth.refreshToken';
const ACCESS_TOKEN_EXPIRES_AT_KEY = 'rpa.auth.accessTokenExpiresAtUtc';
const ROLES_KEY = 'rpa.auth.roles';

/**
 * Kimlik doğrulama servisi. Spec Bölüm 10 — AD/LDAP SSO + JWT.
 * Task 1.3.1'de tanımlanan POST /api/auth/login endpoint'ini çağırır.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly loginUrl = '/api/auth/login';
  private readonly refreshUrl = '/api/auth/refresh';

  constructor(private http: HttpClient) {}

  login(username: string, password: string): Observable<LoginResponse> {
    const body: LoginRequest = { username, password };
    return this.http.post<LoginResponse>(this.loginUrl, body).pipe(
      tap((response) => {
        this.storeSession(response);
      }),
    );
  }

  refreshToken(): Observable<LoginResponse> {
    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      throw new Error('Refresh token bulunamadı.');
    }

    const body: RefreshRequest = { refreshToken };
    return this.http.post<LoginResponse>(this.refreshUrl, body).pipe(
      tap((response) => {
        this.storeSession(response);
      }),
    );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(ACCESS_TOKEN_EXPIRES_AT_KEY);
    localStorage.removeItem(ROLES_KEY);
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  getRoles(): string[] {
    const raw = localStorage.getItem(ROLES_KEY);
    return raw ? (JSON.parse(raw) as string[]) : [];
  }

  /** JWT `sub` claim'inden kullanıcı adını çözer (yalnızca gösterim amaçlı). */
  getUsername(): string | null {
    const token = this.getToken();
    if (!token) {
      return null;
    }
    try {
      const payload = token.split('.')[1];
      const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
      const claims = JSON.parse(json) as { sub?: string };
      return claims.sub ?? null;
    } catch {
      return null;
    }
  }

  private storeSession(response: LoginResponse): void {
    localStorage.setItem(TOKEN_KEY, response.token);
    localStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);
    localStorage.setItem(ACCESS_TOKEN_EXPIRES_AT_KEY, response.accessTokenExpiresAtUtc);
    this.setRoles(response.roles);
  }

  private setRoles(roles: string[]): void {
    localStorage.setItem(ROLES_KEY, JSON.stringify(roles ?? []));
  }
}
