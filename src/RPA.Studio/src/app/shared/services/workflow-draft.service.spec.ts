import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorkflowDraftService } from './workflow-draft.service';
import { emptyWorkflow } from '../models/workflow.model';

describe('WorkflowDraftService', () => {
  let service: WorkflowDraftService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WorkflowDraftService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('load parses the draft JsonDefinition into a WorkflowVersion', () => {
    const wf = emptyWorkflow('w1', 'Sipariş');
    let result: unknown;
    service.load('w1').subscribe((r) => (result = r));

    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify(wf),
    });

    expect(result).toEqual(wf);
  });

  it('save PUTs the serialized graph to the draft endpoint', () => {
    const wf = emptyWorkflow('w1', 'Sipariş');
    service.save('w1', wf).subscribe();

    const req = http.expectOne('/api/workflows/w1/draft');
    expect(req.request.method).toBe('PUT');
    expect(JSON.parse(req.request.body.jsonDefinition)).toEqual(wf);
    req.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
  });

  it('keeps the existing pending hand-off behaviour', () => {
    const wf = emptyWorkflow();
    service.setPending(wf);
    expect(service.consumePending()).toEqual(wf);
    expect(service.consumePending()).toBeNull();
  });
});
