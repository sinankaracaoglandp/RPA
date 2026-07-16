import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LicensePageComponent } from './license-page.component';
import { InstallationRequestDocument, LicenseStatus } from './license.models';

const status: LicenseStatus = {
  isInstalled: true,
  isValid: true,
  licenseId: 'LIC-1',
  revision: 3,
  customerId: 'ACME A.S.',
  expiresAt: '2027-01-31T00:00:00+00:00',
  maxActivatedAgents: 5,
  activatedAgents: 2,
  features: ['edition:enterprise', 'sap', 'vision'],
  errorCode: null,
};

const request: InstallationRequestDocument = {
  schemaVersion: 1,
  installationId: 'INST-1',
  installationPublicKey: 'PUBKEY',
  installationPublicKeyFingerprint: 'FP-1',
  productId: 'rpa-platform',
  customerReference: null,
  createdAt: '2026-07-16T00:00:00+00:00',
};

describe('LicensePageComponent', () => {
  let fixture: ComponentFixture<LicensePageComponent>;
  let httpMock: HttpTestingController;

  const text = () => fixture.nativeElement.textContent as string;
  const el = (testId: string): HTMLElement =>
    fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LicensePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(LicensePageComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const loadStatus = (body: LicenseStatus = status) => {
    fixture.detectChanges();
    httpMock.expectOne('/api/license/status').flush(body);
    fixture.detectChanges();
  };

  it('shows customer, edition, validity, features and seat usage', () => {
    loadStatus();

    expect(el('license-customer').textContent).toContain('ACME A.S.');
    expect(el('license-edition').textContent!.toLowerCase()).toContain('enterprise');
    expect(el('license-seats').textContent).toContain('2 / 5');
    const features = fixture.nativeElement.querySelectorAll('[data-testid="license-feature"]');
    expect(features.length).toBe(3);
    expect(el('license-expires')).toBeTruthy();
    expect(el('license-validity')).toBeTruthy();
  });

  it('renders the not-installed state without seat details', () => {
    loadStatus({
      isInstalled: false,
      isValid: false,
      licenseId: null,
      revision: null,
      customerId: null,
      expiresAt: null,
      maxActivatedAgents: 0,
      activatedAgents: 0,
      features: [],
      errorCode: 'NotInstalled',
    });

    expect(el('license-missing')).toBeTruthy();
    expect(el('license-customer')).toBeFalsy();
  });

  it('renders a stable API error message when status loading fails', () => {
    fixture.detectChanges();
    httpMock
      .expectOne('/api/license/status')
      .flush({ error: 'Lisans okunamadi.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert).toBeTruthy();
    expect(alert.textContent).toContain('Lisans okunamadi.');
  });

  it('downloads the installation request and revokes the object URL', () => {
    loadStatus();

    const createSpy = vi.fn(() => 'blob:fake');
    const revokeSpy = vi.fn();
    (URL as unknown as { createObjectURL: unknown }).createObjectURL = createSpy;
    (URL as unknown as { revokeObjectURL: unknown }).revokeObjectURL = revokeSpy;

    el('download-request').click();
    httpMock.expectOne('/api/license/installation-request').flush(request);
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalledTimes(1);
    expect(revokeSpy).toHaveBeenCalledWith('blob:fake');
  });

  it('restricts the license file input to .lic and json', () => {
    loadStatus();

    const input = el('license-file-input') as unknown as HTMLInputElement;
    expect(input.getAttribute('accept')).toBe('.lic,application/json');
    expect(input.hidden || input.classList.contains('hidden')).toBe(true);
  });

  it('imports an uploaded .lic file and refreshes status', async () => {
    loadStatus();

    const file = new File([JSON.stringify({ Payload: { LicenseId: 'LIC-1' }, Signature: 'sig' })], 'a.lic', {
      type: 'application/json',
    });
    await fixture.componentInstance.importFile(file);
    fixture.detectChanges();

    const importReq = httpMock.expectOne('/api/license/import');
    expect(importReq.request.method).toBe('POST');
    expect(importReq.request.body.Signature).toBe('sig');
    importReq.flush(status);
    fixture.detectChanges();

    httpMock.expectOne('/api/license/status').flush(status);
    fixture.detectChanges();
    expect(el('import-success')).toBeTruthy();
  });

  it('renders the API error body when import is rejected', async () => {
    loadStatus();

    const file = new File([JSON.stringify({ Payload: {}, Signature: 'x' })], 'a.lic');
    await fixture.componentInstance.importFile(file);
    fixture.detectChanges();

    httpMock
      .expectOne('/api/license/import')
      .flush({ error: 'Lisans imzasi gecersiz.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el('import-error').textContent).toContain('Lisans imzasi gecersiz.');
  });

  it('reports a parse error for a non-JSON license file without calling the API', async () => {
    loadStatus();

    await fixture.componentInstance.importFile(new File(['not-json'], 'a.lic'));
    fixture.detectChanges();

    expect(el('import-error')).toBeTruthy();
    httpMock.expectNone('/api/license/import');
  });

  it('never writes license or secret payloads to local/session storage', async () => {
    const localSpy = vi.spyOn(Storage.prototype, 'setItem');
    loadStatus();

    el('download-request').click();
    httpMock.expectOne('/api/license/installation-request').flush(request);
    await fixture.componentInstance.importFile(
      new File([JSON.stringify({ Payload: { LicenseId: 'LIC-1' }, Signature: 'sig' })], 'a.lic'),
    );
    httpMock.expectOne('/api/license/import').flush(status);
    httpMock.expectOne('/api/license/status').flush(status);
    fixture.detectChanges();

    for (const [key, value] of localSpy.mock.calls) {
      expect(`${key} ${value}`).not.toContain('LIC-1');
      expect(`${key} ${value}`).not.toContain('sig');
      expect(`${key} ${value}`).not.toContain('PUBKEY');
    }
    expect(JSON.stringify(localStorage)).not.toContain('LIC-1');
    expect(JSON.stringify(sessionStorage)).not.toContain('LIC-1');
    localSpy.mockRestore();
  });
});
