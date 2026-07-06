import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { JobsComponent } from './jobs.component';
import { JobRunListResponse } from '../orchestrator.models';

describe('JobsComponent', () => {
  let fixture: ComponentFixture<JobsComponent>;
  let component: JobsComponent;
  let httpMock: HttpTestingController;

  const resp: JobRunListResponse = {
    totalCount: 1,
    items: [{
      id: 'j1', workflowVersionId: 'w1', status: 'Failed', triggeredBy: 'cron',
      assignedRobotId: null, environmentId: 'e1', startedAt: '2026-07-06T10:00:00Z',
      completedAt: null, correlationId: 'corr-1',
    }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(JobsComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads and renders job rows', () => {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/jobruns').flush(resp);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="job-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Failed');
  });

  it('re-queries with status filter', () => {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/jobruns').flush({ totalCount: 0, items: [] });

    component.statusFilter = 'Successful';
    component.load();

    const req = httpMock.expectOne((r) => r.url === '/api/jobruns');
    expect(req.request.params.get('status')).toBe('Successful');
    req.flush({ totalCount: 0, items: [] });
  });
});
