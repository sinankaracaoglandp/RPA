import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AgentLicensePageComponent } from './agent-license-page.component';
import { AgentDto } from './agent-license.models';
import { LicenseStatus } from '../licensing/license.models';

const seatStatus = (activated: number, max = 3): LicenseStatus => ({
  isInstalled: true,
  isValid: true,
  licenseId: 'LIC-1',
  revision: 1,
  customerId: 'ACME',
  customerName: 'ACME Sanayi A.S.',
  edition: 'enterprise',
  expiresAt: '2027-01-31T00:00:00+00:00',
  maxActivatedAgents: max,
  activatedAgents: activated,
  features: [],
  errorCode: null,
});

const agents: AgentDto[] = [
  { id: 'a1', name: 'Agent Bir', status: 'Activated', machineFingerprint: 'FP-1', lastSeenAt: '2026-07-16T10:00:00+00:00' },
  { id: 'a2', name: 'Agent Iki', status: 'PendingActivation', machineFingerprint: null, lastSeenAt: null },
  { id: 'a3', name: 'Agent Uc', status: 'Disabled', machineFingerprint: 'FP-3', lastSeenAt: null },
  { id: 'a4', name: 'Agent Dort', status: 'Deactivated', machineFingerprint: null, lastSeenAt: null },
];

describe('AgentLicensePageComponent', () => {
  let fixture: ComponentFixture<AgentLicensePageComponent>;
  let httpMock: HttpTestingController;

  const el = (testId: string): HTMLElement =>
    fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  const all = (testId: string): HTMLElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll(`[data-testid="${testId}"]`));

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AgentLicensePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(AgentLicensePageComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  /** Ilk yukleme: hem agent listesi hem yetkili koltuk durumu API'den gelir. */
  const load = (list: AgentDto[] = agents, status: LicenseStatus = seatStatus(2)) => {
    fixture.detectChanges();
    httpMock.expectOne('/api/agents').flush(list);
    httpMock.expectOne('/api/license/status').flush(status);
    fixture.detectChanges();
  };

  it('renders a state badge for every agent state', () => {
    load();

    const badges = all('agent-status');
    expect(badges.length).toBe(4);
    expect(badges[0].textContent).toContain('Activated');
    expect(badges[1].textContent).toContain('PendingActivation');
    expect(badges[2].textContent).toContain('Disabled');
    expect(badges[3].textContent).toContain('Deactivated');
  });

  it('shows authoritative seat usage from the license API', () => {
    load(agents, seatStatus(2, 3));

    expect(el('agent-seats').textContent).toContain('2 / 3');
  });

  it('creates a pending agent and refreshes list and seats', () => {
    load();

    (el('agent-name-input') as unknown as HTMLInputElement).value = 'Yeni Agent';
    (el('agent-name-input') as unknown as HTMLInputElement).dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el('create-agent').click();

    const create = httpMock.expectOne('/api/agents');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ name: 'Yeni Agent' });
    create.flush({ id: 'a5', name: 'Yeni Agent', status: 'PendingActivation', machineFingerprint: null, lastSeenAt: null });

    httpMock.expectOne('/api/agents').flush([...agents]);
    httpMock.expectOne('/api/license/status').flush(seatStatus(2));
    fixture.detectChanges();
  });

  it('shows the activation code exactly once and clears it on close', () => {
    load();

    all('activation-code-button')[1].click();
    httpMock
      .expectOne('/api/agents/a2/activation-code')
      .flush({ agentId: 'a2', activationCode: 'PLAINTEXT-CODE', expiresAt: '2026-07-16T10:15:00+00:00' });
    httpMock.expectOne('/api/agents').flush(agents);
    httpMock.expectOne('/api/license/status').flush(seatStatus(2));
    fixture.detectChanges();

    expect(el('activation-code-value').textContent).toContain('PLAINTEXT-CODE');

    el('activation-code-close').click();
    fixture.detectChanges();

    expect(el('activation-code-value')).toBeFalsy();
    expect(fixture.nativeElement.textContent).not.toContain('PLAINTEXT-CODE');

    // Ikinci gosterim yok: modal state yalnizca bellekte, yeniden acilmaz.
    fixture.componentInstance.ngOnDestroy();
    expect(fixture.componentInstance.activationCode()).toBeNull();
  });

  it('asks for confirmation before disabling and keeps the seat consumed', () => {
    load();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    all('disable-agent')[0].click();

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    const disableReq = httpMock.expectOne('/api/agents/a1/disable');
    expect(disableReq.request.method).toBe('POST');
    disableReq.flush(null);

    // Mutasyon sonrasi koltuk durumu API'den yeniden okunur (istemcide hesaplanmaz).
    httpMock.expectOne('/api/agents').flush(
      agents.map((a) => (a.id === 'a1' ? { ...a, status: 'Disabled' as const } : a)),
    );
    httpMock.expectOne('/api/license/status').flush(seatStatus(2));
    fixture.detectChanges();

    // Disabled koltuk tuketir: sunucu 2/3 dedi, ekran onu gosterir.
    expect(el('agent-seats').textContent).toContain('2 / 3');
    expect(all('agent-status')[0].textContent).toContain('Disabled');
  });

  it('does not call the API when disable confirmation is declined', () => {
    load();
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    all('disable-agent')[0].click();

    httpMock.expectNone('/api/agents/a1/disable');
  });

  it('asks for confirmation before deactivating and releases the seat after API refresh', () => {
    load();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    all('deactivate-agent')[0].click();

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    httpMock.expectOne('/api/agents/a1/deactivate').flush(null);

    httpMock.expectOne('/api/agents').flush(
      agents.map((a) => (a.id === 'a1' ? { ...a, status: 'Deactivated' as const } : a)),
    );
    httpMock.expectOne('/api/license/status').flush(seatStatus(1));
    fixture.detectChanges();

    // Deactivated koltugu birakir: yetkili sayim 1/3.
    expect(el('agent-seats').textContent).toContain('1 / 3');
    expect(all('agent-status')[0].textContent).toContain('Deactivated');
  });

  it('does not call the API when deactivate confirmation is declined', () => {
    load();
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    all('deactivate-agent')[0].click();

    httpMock.expectNone('/api/agents/a1/deactivate');
  });

  it('renders a stable API error message when the agent list fails', () => {
    fixture.detectChanges();
    httpMock
      .expectOne('/api/agents')
      .flush({ error: 'Agentlar okunamadi.' }, { status: 500, statusText: 'Server Error' });
    httpMock.expectOne('/api/license/status').flush(seatStatus(0));
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert.textContent).toContain('Agentlar okunamadi.');
  });

  it('never writes an activation code or agent secret to local/session storage', () => {
    const setItemSpy = vi.spyOn(Storage.prototype, 'setItem');
    load();

    all('activation-code-button')[1].click();
    httpMock
      .expectOne('/api/agents/a2/activation-code')
      .flush({ agentId: 'a2', activationCode: 'PLAINTEXT-CODE', expiresAt: '2026-07-16T10:15:00+00:00' });
    httpMock.expectOne('/api/agents').flush(agents);
    httpMock.expectOne('/api/license/status').flush(seatStatus(2));
    fixture.detectChanges();

    for (const [key, value] of setItemSpy.mock.calls) {
      expect(`${key} ${value}`).not.toContain('PLAINTEXT-CODE');
    }
    expect(JSON.stringify(localStorage)).not.toContain('PLAINTEXT-CODE');
    expect(JSON.stringify(sessionStorage)).not.toContain('PLAINTEXT-CODE');
  });
});
