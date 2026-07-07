import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProjectService } from './project.service';

describe('ProjectService', () => {
  let service: ProjectService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProjectService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists projects from GET /api/projects', () => {
    let result: unknown;
    service.getProjects().subscribe((r) => (result = r));

    const req = http.expectOne('/api/projects');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'p1', name: 'Pilot', workflowCount: 2 }]);

    expect(result).toEqual([{ id: 'p1', name: 'Pilot', workflowCount: 2 }]);
  });

  it('creates a project via POST /api/projects', () => {
    service.createProject('Pilot', 'açıklama').subscribe();
    const req = http.expectOne('/api/projects');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Pilot', description: 'açıklama' });
    req.flush({ id: 'p1', name: 'Pilot', workflowCount: 0 });
  });

  it('creates a workflow via POST /api/projects/{id}/workflows', () => {
    service.createWorkflow('p1', 'Sipariş').subscribe();
    const req = http.expectOne('/api/projects/p1/workflows');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Sipariş' });
    req.flush({ id: 'w1', name: 'Sipariş' });
  });
});
