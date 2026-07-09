import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { DesignerComponent } from './designer.component';
import { ModeService } from '../../shared/services/mode.service';
import { WorkflowDraftService } from '../../shared/services/workflow-draft.service';

describe('DesignerComponent — Simple Mode integration', () => {
  let fixture: ComponentFixture<DesignerComponent>;
  let component: DesignerComponent;
  let httpMock: HttpTestingController;
  let modeService: ModeService;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DesignerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DesignerComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    modeService = TestBed.inject(ModeService);
  });

  afterEach(() => {
    // Activity catalog requests fired by whichever toolbox is active.
    httpMock.match('/api/activities').forEach((req) => req.flush([]));
  });

  it('shows the full toolbox and debug toggle in Advanced mode', () => {
    modeService.setMode('Advanced');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-toolbox')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-simplified-toolbox')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="designer-debug-toggle"]')).toBeTruthy();
  });

  it('hides the debug toggle/panel and full toolbox in Simple mode', () => {
    modeService.setMode('Simple');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-simplified-toolbox')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-toolbox')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="designer-debug-toggle"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('app-debug-panel')).toBeFalsy();
  });

  it('navigates back to projects from the header button', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="designer-back-to-projects"]').click();

    expect(navigate).toHaveBeenCalledWith(['/projects']);
  });

  it('does not set breakpoints when a node is selected in Simple mode', () => {
    modeService.setMode('Simple');
    fixture.detectChanges();
    component.debugMode.set(true);

    component.onNodeSelect('node-1');

    expect(component.breakpointNodeIds()).toEqual([]);
  });

  it('toggleDebug is a no-op in Simple mode', async () => {
    modeService.setMode('Simple');
    fixture.detectChanges();

    await component.toggleDebug();

    expect(component.debugMode()).toBe(false);
  });

  it('loads a pending draft workflow from the Template Wizard hand-off on construction', () => {
    const draft = TestBed.inject(WorkflowDraftService);
    const workflow = {
      schemaVersion: '1.0',
      id: 'draft-1',
      name: 'From Template',
      version: '1.0.0',
      nodes: [],
      connections: [],
    };
    draft.setPending(workflow);

    const freshFixture = TestBed.createComponent(DesignerComponent);
    expect(freshFixture.componentInstance.workflow()).toEqual(workflow);
  });
});

describe('draft persistence (Paket B)', () => {
  let fixture: ComponentFixture<DesignerComponent>;
  let component: DesignerComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DesignerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ workflowId: 'w1' }) } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DesignerComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.match('/api/activities').forEach((req) => req.flush([]));
  });

  it('loads the draft for the routed workflowId on init', () => {
    fixture.detectChanges();
    const req = http.expectOne('/api/workflows/w1/draft');
    req.flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    fixture.detectChanges();

    expect(component.workflow()?.name).toBe('Sipariş');
    expect(component.dirty()).toBe(false);
  });

  it('marks dirty when the graph changes and clears it after save', () => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });

    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });
    expect(component.dirty()).toBe(true);

    void component.save();
    const put = http.expectOne('/api/workflows/w1/draft');
    expect(put.request.method).toBe('PUT');
    put.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });

    expect(component.dirty()).toBe(false);
  });

  it('saves declared workflow variables with the draft json', () => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'SipariÅŸ', version: '1.0.0',
        nodes: [], connections: [], variables: [],
      }),
    });

    component.onVariablesChange([
      { name: 'okunanMetin', type: 'string', scope: 'global', default: '' },
    ]);
    expect(component.dirty()).toBe(true);

    void component.save();
    const put = http.expectOne('/api/workflows/w1/draft');
    const json = JSON.parse(put.request.body.jsonDefinition);
    expect(json.variables).toEqual([
      { name: 'okunanMetin', type: 'string', scope: 'global', default: '' },
    ]);
    put.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
  });

  it('sets saveState to error when the save fails', () => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });

    void component.save();
    http.expectOne('/api/workflows/w1/draft').flush(
      { error: 'şema hatası' }, { status: 400, statusText: 'Bad Request' },
    );

    expect(component.saveState()).toBe('error');
    expect(component.dirty()).toBe(true);
  });

  it('saves the draft before queuing a run', fakeAsync(() => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });

    void component.run();

    const put = http.expectOne('/api/workflows/w1/draft');
    expect(put.request.method).toBe('PUT');
    put.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
    tick();

    const run = http.expectOne('/api/workflows/w1/run');
    expect(run.request.method).toBe('POST');
    run.flush({ queueItemId: '12345678-0000-0000-0000-000000000000', queueId: 'q1', status: 'New' });
    fixture.detectChanges();

    expect(component.runState()).toBe('queued');
    expect(component.lastQueueItemId()).toBe('12345678-0000-0000-0000-000000000000');
    expect(component.lastQueueId()).toBe('q1');
    expect(component.lastRunStatus()).toBe('New');
    expect(fixture.nativeElement.querySelector('[data-testid="designer-run-queue-item"]').textContent)
      .toContain('12345678');
    expect(fixture.nativeElement.querySelector('[data-testid="designer-run-status"]').textContent)
      .toContain('New');
    fixture.destroy();
    discardPeriodicTasks();
  }));

  it('polls the queued run status until it reaches a terminal status', fakeAsync(() => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });

    void component.run();
    http.expectOne('/api/workflows/w1/draft').flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
    tick();
    http.expectOne('/api/workflows/w1/run').flush({
      queueItemId: 'qi1',
      queueId: 'q1',
      status: 'New',
    });

    tick(3000);
    http.expectOne('/api/queues/q1/items/qi1').flush({
      id: 'qi1',
      queueId: 'q1',
      status: 'Successful',
      attemptCount: 1,
      assignedRobotId: null,
      payload: '{}',
      errorDetail: null,
    });

    expect(component.lastRunStatus()).toBe('Successful');
    tick(3000);
    http.expectNone('/api/queues/q1/items/qi1');
  }));

  it('stops polling when the component is destroyed', fakeAsync(() => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });

    void component.run();
    http.expectOne('/api/workflows/w1/draft').flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
    tick();
    http.expectOne('/api/workflows/w1/run').flush({
      queueItemId: 'qi1',
      queueId: 'q1',
      status: 'New',
    });

    fixture.destroy();
    tick(3000);
    http.expectNone('/api/queues/q1/items/qi1');
  }));

  it('treats abandoned as a terminal run status', fakeAsync(() => {
    component.lastQueueId.set('q1');
    component.lastQueueItemId.set('qi1');
    component['startRunStatusPolling']('q1', 'qi1');

    tick(3000);
    http.expectOne('/api/queues/q1/items/qi1').flush({
      id: 'qi1',
      queueId: 'q1',
      status: 'Abandoned',
      attemptCount: 1,
      assignedRobotId: null,
      payload: '{}',
      errorDetail: null,
    });

    expect(component.lastRunStatus()).toBe('Abandoned');
    tick(3000);
    http.expectNone('/api/queues/q1/items/qi1');
  }));

  it('refreshes the queued run status', () => {
    component.lastQueueId.set('q1');
    component.lastQueueItemId.set('qi1');

    component.refreshRunStatus();

    const req = http.expectOne('/api/queues/q1/items/qi1');
    expect(req.request.method).toBe('GET');
    req.flush({
      id: 'qi1',
      queueId: 'q1',
      status: 'Successful',
      attemptCount: 1,
      assignedRobotId: null,
      payload: '{}',
      errorDetail: null,
    });

    expect(component.lastRunStatus()).toBe('Successful');
  });
});
