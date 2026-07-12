import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { LoginResponse } from './auth.models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('is not authenticated when no token is stored', () => {
    expect(service.isAuthenticated()).toBe(false);
  });

  it('stores the JWT token and roles in localStorage on successful login', () => {
    const response: LoginResponse = {
      token: 'jwt-abc-123',
      refreshToken: 'refresh-abc-123',
      accessTokenExpiresAtUtc: '2026-07-10T18:00:00Z',
      roles: ['Developer'],
    };

    service.login('sinan', 'secret').subscribe((res) => {
      expect(res.token).toBe('jwt-abc-123');
    });

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'sinan', password: 'secret' });
    req.flush(response);

    expect(service.getToken()).toBe('jwt-abc-123');
    expect(service.getRefreshToken()).toBe('refresh-abc-123');
    expect(service.getRoles()).toEqual(['Developer']);
    expect(service.isAuthenticated()).toBe(true);
  });

  it('refreshes and stores a new token pair', () => {
    localStorage.setItem('rpa.auth.refreshToken', 'refresh-old');

    service.refreshToken().subscribe((res) => {
      expect(res.token).toBe('jwt-new');
    });

    const req = httpMock.expectOne('/api/auth/refresh');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ refreshToken: 'refresh-old' });
    req.flush({
      token: 'jwt-new',
      refreshToken: 'refresh-new',
      accessTokenExpiresAtUtc: '2026-07-10T18:05:00Z',
      roles: ['Developer'],
    });

    expect(service.getToken()).toBe('jwt-new');
    expect(service.getRefreshToken()).toBe('refresh-new');
  });

  it('clears token on logout', () => {
    localStorage.setItem('rpa.auth.token', 'jwt-abc-123');
    localStorage.setItem('rpa.auth.refreshToken', 'refresh-abc-123');
    service.logout();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.getRefreshToken()).toBeNull();
  });
});
