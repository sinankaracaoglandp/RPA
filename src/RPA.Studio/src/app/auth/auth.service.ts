import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from './auth.models';

const TOKEN_KEY = 'rpa.auth.token';
const ROLES_KEY = 'rpa.auth.roles';

/**
 * Kimlik doğrulama servisi. Spec Bölüm 10 — AD/LDAP SSO + JWT.
 * Task 1.3.1'de tanımlanan POST /api/auth/login endpoint'ini çağırır.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly loginUrl = '/api/auth/login';

  constructor(private http: HttpClient) {}

  login(username: string, password: string): Observable<LoginResponse> {
    const body: LoginRequest = { username, password };
    return this.http.post<LoginResponse>(this.loginUrl, body).pipe(
      tap((response) => {
        this.setToken(response.token);
        this.setRoles(response.roles);
      }),
    );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ROLES_KEY);
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  getRoles(): string[] {
    const raw = localStorage.getItem(ROLES_KEY);
    return raw ? (JSON.parse(raw) as string[]) : [];
  }

  private setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
  }

  private setRoles(roles: string[]): void {
    localStorage.setItem(ROLES_KEY, JSON.stringify(roles ?? []));
  }
}
