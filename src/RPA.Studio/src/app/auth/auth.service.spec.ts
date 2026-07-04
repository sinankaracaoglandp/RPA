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
    const response: LoginResponse = { token: 'jwt-abc-123', roles: ['Developer'] };

    service.login('sinan', 'secret').subscribe((res) => {
      expect(res.token).toBe('jwt-abc-123');
    });

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'sinan', password: 'secret' });
    req.flush(response);

    expect(service.getToken()).toBe('jwt-abc-123');
    expect(service.getRoles()).toEqual(['Developer']);
    expect(service.isAuthenticated()).toBe(true);
  });

  it('clears token on logout', () => {
    localStorage.setItem('rpa.auth.token', 'jwt-abc-123');
    service.logout();
    expect(service.isAuthenticated()).toBe(false);
  });
});
