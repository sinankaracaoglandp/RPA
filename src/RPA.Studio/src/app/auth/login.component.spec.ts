import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('marks the form invalid when username and password are empty', () => {
    expect(component.form.invalid).toBe(true);
  });

  it('shows validation errors when submit is clicked with empty fields', () => {
    const submitButton: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="submit-button"]',
    );

    submitButton.click();
    fixture.detectChanges();

    expect(component.form.controls.username.touched).toBe(true);
    expect(component.form.controls.password.touched).toBe(true);

    const usernameError = fixture.nativeElement.querySelector('[data-testid="username-error"]');
    const passwordError = fixture.nativeElement.querySelector('[data-testid="password-error"]');
    expect(usernameError).toBeTruthy();
    expect(passwordError).toBeTruthy();
  });

  it('does not call AuthService.login when the form is invalid', () => {
    component.submit();
    httpMock.expectNone('/api/auth/login');
  });

  it('calls AuthService.login and navigates on valid submit', () => {
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    component.form.setValue({ username: 'sinan', password: 'secret' });
    component.submit();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    req.flush({
      token: 'jwt-abc',
      refreshToken: 'refresh-abc',
      accessTokenExpiresAtUtc: '2026-07-10T18:00:00Z',
      roles: ['Developer'],
    });

    expect(navigateSpy).toHaveBeenCalledWith('/');
  });

  it('shows an error message when login fails with 401', () => {
    component.form.setValue({ username: 'sinan', password: 'wrong' });
    component.submit();

    const req = httpMock.expectOne('/api/auth/login');
    req.flush({ error: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    fixture.detectChanges();
    expect(component.errorMessage()).toBeTruthy();
  });
});
