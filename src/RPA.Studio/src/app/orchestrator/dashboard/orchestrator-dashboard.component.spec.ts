import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OrchestratorDashboardComponent } from './orchestrator-dashboard.component';
import { DashboardSummary } from '../orchestrator.models';

describe('OrchestratorDashboardComponent', () => {
  let fixture: ComponentFixture<OrchestratorDashboardComponent>;
  let httpMock: HttpTestingController;

  const summary: DashboardSummary = {
    total: 10, running: 2, successful: 6, failed: 1,
    businessException: 1, abandoned: 0, successRate: 75,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OrchestratorDashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(OrchestratorDashboardComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders summary cards after load', () => {
    fixture.detectChanges(); // triggers ngOnInit → GET
    httpMock.expectOne('/api/jobruns/dashboard').flush(summary);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="total"]')!.textContent).toContain('10');
    expect(el.querySelector('[data-testid="successful"]')!.textContent).toContain('6');
    expect(el.querySelector('[data-testid="success-rate"]')!.textContent).toContain('75');
  });

  it('shows an error message when the request fails', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/jobruns/dashboard').error(new ProgressEvent('fail'));
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert).toBeTruthy();
  });
});
