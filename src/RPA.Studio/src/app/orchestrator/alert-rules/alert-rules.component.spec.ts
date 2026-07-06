import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AlertRulesComponent } from './alert-rules.component';
import { AlertRule } from '../orchestrator.models';

describe('AlertRulesComponent', () => {
  let fixture: ComponentFixture<AlertRulesComponent>;
  let component: AlertRulesComponent;
  let httpMock: HttpTestingController;

  const rules: AlertRule[] = [{
    id: 'r1', name: 'SysExc', condition: '{"metric":"SystemExceptionCount","threshold":5}',
    channel: 'email', recipients: 'ops@example.com', isActive: true,
  }];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AlertRulesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(AlertRulesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders rule rows', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/alert-rules').flush(rules);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="rule-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('SysExc');
  });

  it('creates a rule with composed condition JSON', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/alert-rules').flush([]);

    component.form = { name: 'New', metric: 'RobotOfflineCount', threshold: 2, channel: 'teams', recipients: 'https://hook' };
    component.create();

    const req = httpMock.expectOne((r) => r.url === '/api/alert-rules' && r.method === 'POST');
    expect(req.request.body.name).toBe('New');
    expect(JSON.parse(req.request.body.condition)).toEqual({ metric: 'RobotOfflineCount', threshold: 2 });
    req.flush({ ...rules[0], id: 'r2', name: 'New' });

    // reload
    httpMock.expectOne('/api/alert-rules').flush([]);
  });

  it('toggles active state', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/alert-rules').flush(rules);
    fixture.detectChanges();

    component.toggle(rules[0]);
    const req = httpMock.expectOne('/api/alert-rules/r1/active');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ isActive: false });
    req.flush({ ...rules[0], isActive: false });

    httpMock.expectOne('/api/alert-rules').flush([]);
  });
});
