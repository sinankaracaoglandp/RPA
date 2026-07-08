import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrchestratorService } from './orchestrator.service';
import { DashboardSummary, JobRunListResponse } from './orchestrator.models';

describe('OrchestratorService', () => {
  let service: OrchestratorService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(OrchestratorService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches dashboard summary', () => {
    const summary: DashboardSummary = {
      total: 4, running: 1, successful: 2, failed: 1,
      businessException: 0, abandoned: 0, successRate: 66.67,
    };
    service.getDashboard().subscribe((s) => expect(s.successRate).toBe(66.67));

    const req = httpMock.expectOne('/api/jobruns/dashboard');
    expect(req.request.method).toBe('GET');
    req.flush(summary);
  });

  it('passes environmentId to dashboard when provided', () => {
    service.getDashboard('env-1').subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/jobruns/dashboard');
    expect(req.request.params.get('environmentId')).toBe('env-1');
    req.flush({} as DashboardSummary);
  });

  it('lists jobs with status + paging filters', () => {
    const resp: JobRunListResponse = { totalCount: 0, items: [] };
    service.listJobs({ status: 'Failed', skip: 10, take: 25 }).subscribe((r) =>
      expect(r.totalCount).toBe(0),
    );

    const req = httpMock.expectOne((r) => r.url === '/api/jobruns');
    expect(req.request.params.get('status')).toBe('Failed');
    expect(req.request.params.get('skip')).toBe('10');
    expect(req.request.params.get('take')).toBe('25');
    req.flush(resp);
  });

  it('gets a single job by id (encoded)', () => {
    service.getJob('abc/1').subscribe();
    const req = httpMock.expectOne('/api/jobruns/abc%2F1');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('lists robots', () => {
    service.listRobots().subscribe((r) => expect(r.length).toBe(1));
    const req = httpMock.expectOne('/api/robots');
    req.flush([{ id: '1', machineName: 'RPA-01', mode: 'Unattended', status: 'Online' }]);
  });

  it('creates an alert rule', () => {
    service.createAlertRule({ name: 'r', condition: '{}', channel: 'email', recipients: 'a@b.c', isActive: true }).subscribe();
    const req = httpMock.expectOne('/api/alert-rules');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('toggles alert rule active state', () => {
    service.setAlertRuleActive('r1', false).subscribe();
    const req = httpMock.expectOne('/api/alert-rules/r1/active');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ isActive: false });
    req.flush({});
  });

  it('lists action items with type filter', () => {
    service.listActionItems('Approval').subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/action-center');
    expect(req.request.params.get('type')).toBe('Approval');
    req.flush([]);
  });

  it('resolves an action item with a note', () => {
    service.resolveActionItem('a1', 'ok').subscribe();
    const req = httpMock.expectOne('/api/action-center/a1/resolve');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ note: 'ok' });
    req.flush({});
  });

  it('lists queues', () => {
    service.listQueues().subscribe((q) => expect(q.length).toBe(1));
    const req = httpMock.expectOne('/api/queues');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'q1', name: 'Q1', maxRetries: 3, slaSeconds: null, newCount: 0, inProgressCount: 0, failedCount: 0, total: 0 }]);
  });

  it('lists queue items with status filter', () => {
    service.listQueueItems('q1', 'Failed', 0, 50).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/queues/q1/items');
    expect(req.request.params.get('status')).toBe('Failed');
    expect(req.request.params.get('take')).toBe('50');
    req.flush({ totalCount: 0, items: [] });
  });

  it('gets a single queue item by queue and item id', () => {
    service.getQueueItem('q/1', 'i/1').subscribe((item) => expect(item.status).toBe('Successful'));
    const req = httpMock.expectOne('/api/queues/q%2F1/items/i%2F1');
    expect(req.request.method).toBe('GET');
    req.flush({
      id: 'i/1',
      queueId: 'q/1',
      status: 'Successful',
      attemptCount: 1,
      assignedRobotId: null,
      payload: '{}',
      errorDetail: null,
    });
  });

  it('lists environments', () => {
    service.listEnvironments().subscribe((e) => expect(e.length).toBe(1));
    const req = httpMock.expectOne('/api/environments');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'e1', name: 'Dev', description: '' }]);
  });

  it('creates an environment', () => {
    service.createEnvironment('Staging', 'x').subscribe();
    const req = httpMock.expectOne('/api/environments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Staging', description: 'x' });
    req.flush({});
  });

  it('publishes a workflow version to test', () => {
    service.publishWorkflowVersion('wf1', '1.0.0').subscribe();
    const req = httpMock.expectOne('/api/workflows/wf1/versions/1.0.0/publish');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('approves a workflow version to prod', () => {
    service.approveWorkflowVersion('wf1', '1.0.0').subscribe();
    const req = httpMock.expectOne('/api/workflows/wf1/versions/1.0.0/approve');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });
});
