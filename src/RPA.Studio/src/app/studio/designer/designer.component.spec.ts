import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { DesignerComponent } from './designer.component';
import { ModeService } from '../../shared/services/mode.service';
import { WorkflowDraftService } from '../../shared/services/workflow-draft.service';
import { SPY_HUB_CONNECTION_FACTORY } from '../../shared/services/spy.service';

/** Zoneless ortamda mikrogörev kuyruğunu boşaltır (fakeAsync tick() yerine). */
async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
}

/** RunLogService'in gerçek SignalR bağlantısı açmasını engelleyen no-op hub fabrikası. */
const stubHubConnectionFactory = () => ({
  start: () => Promise.resolve(),
  stop: () => Promise.resolve(),
  on: () => undefined,
  invoke: () => Promise.resolve(),
});

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
    // applyWorkflow, değişken listesini normalize eder (variables: []).
    expect(freshFixture.componentInstance.workflow()).toEqual({ ...workflow, variables: [] });
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
        { provide: SPY_HUB_CONNECTION_FACTORY, useValue: stubHubConnectionFactory },
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

  it('registers pinned profile schema under the requested object root', () => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'E-Fatura', version: '1.0.0',
        nodes: [], connections: [], variables: [],
      }),
    });

    component.onProfileActivityPropertiesChange('EInvoice.ReadProfile', {
      profileId: 'profile-1',
      profileVersion: 2,
      outputVariable: 'fatura',
      outputSchemaJson: JSON.stringify({
        type: 'object',
        properties: {
          faturaNo: { type: 'string' },
          satirlar: { type: 'array', items: { type: 'object', properties: { MalzemeKodu: { type: 'string' } } } },
        },
      }),
    });

    expect(component.variables()).toContainEqual(expect.objectContaining({
      name: 'fatura',
      type: 'object',
      schema: expect.objectContaining({ properties: expect.any(Object) }),
    }));
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

  it('saves the draft before queuing a run', async () => {
    vi.useFakeTimers();
    try {
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
      await flushMicrotasks();

      const put = http.expectOne('/api/workflows/w1/draft');
      expect(put.request.method).toBe('PUT');
      put.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
      await flushMicrotasks();

      const run = http.expectOne('/api/workflows/w1/run');
      expect(run.request.method).toBe('POST');
      run.flush({ queueItemId: '12345678-0000-0000-0000-000000000000', queueId: 'q1', status: 'New' });
      await flushMicrotasks();
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
    } finally {
      vi.useRealTimers();
    }
  });

  it('polls the queued run status until it reaches a terminal status', async () => {
    vi.useFakeTimers();
    try {
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
      await flushMicrotasks();
      http.expectOne('/api/workflows/w1/draft').flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
      await flushMicrotasks();
      http.expectOne('/api/workflows/w1/run').flush({
        queueItemId: 'qi1',
        queueId: 'q1',
        status: 'New',
      });
      await flushMicrotasks();

      await vi.advanceTimersByTimeAsync(3000);
      http.expectOne('/api/queues/q1/items/qi1').flush({
        id: 'qi1',
        queueId: 'q1',
        status: 'Successful',
        attemptCount: 1,
        assignedRobotId: null,
        payload: '{}',
        errorDetail: null,
      });
      await flushMicrotasks();

      expect(component.lastRunStatus()).toBe('Successful');
      await vi.advanceTimersByTimeAsync(3000);
      http.expectNone('/api/queues/q1/items/qi1');
    } finally {
      vi.useRealTimers();
    }
  });

  it('stops polling when the component is destroyed', async () => {
    vi.useFakeTimers();
    try {
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
      await flushMicrotasks();
      http.expectOne('/api/workflows/w1/draft').flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
      await flushMicrotasks();
      http.expectOne('/api/workflows/w1/run').flush({
        queueItemId: 'qi1',
        queueId: 'q1',
        status: 'New',
      });
      await flushMicrotasks();

      fixture.destroy();
      await vi.advanceTimersByTimeAsync(3000);
      http.expectNone('/api/queues/q1/items/qi1');
    } finally {
      vi.useRealTimers();
    }
  });

  it('treats abandoned as a terminal run status', async () => {
    vi.useFakeTimers();
    try {
      component.lastQueueId.set('q1');
      component.lastQueueItemId.set('qi1');
      component['startRunStatusPolling']('q1', 'qi1');

      await vi.advanceTimersByTimeAsync(3000);
      http.expectOne('/api/queues/q1/items/qi1').flush({
        id: 'qi1',
        queueId: 'q1',
        status: 'Abandoned',
        attemptCount: 1,
        assignedRobotId: null,
        payload: '{}',
        errorDetail: null,
      });
      await flushMicrotasks();

      expect(component.lastRunStatus()).toBe('Abandoned');
      await vi.advanceTimersByTimeAsync(3000);
      http.expectNone('/api/queues/q1/items/qi1');
    } finally {
      vi.useRealTimers();
    }
  });

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

describe('DesignerComponent — structured view toggle', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DesignerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    (TestBed.inject(HttpTestingController)).match('/api/activities').forEach((r) => r.flush([]));
  });

  it('toggles between canvas and structured view', () => {
    const fixture = TestBed.createComponent(DesignerComponent);
    const cmp = fixture.componentInstance;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-canvas')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-structured-view')).toBeFalsy();

    cmp.toggleStructuredView();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-structured-view')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-canvas')).toBeFalsy();
  });
});

describe('DesignerComponent — ForEach item variable injection', () => {
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
  });

  it('injects the loop item variable into panelVariables for a body node', () => {
    const fixture = TestBed.createComponent(DesignerComponent);
    const cmp = fixture.componentInstance;
    cmp.variables.set([{
      name: 'faturalar', type: 'list<object>',
      schema: { type: 'array', items: { type: 'object', properties: { tutar: { type: 'number' } } } },
    }]);
    cmp.currentGraph.set({
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [
        { id: 'fe', type: 'forEach', items: '${faturalar}', itemVariable: 'fatura' },
        { id: 'a', type: 'activity' },
      ],
      connections: [
        { from: 'fe', to: 'a', fromPort: 'body' },
        { from: 'a', to: 'fe', toPort: 'loop-back' },
      ],
    });

    cmp.selectedNodeId.set('a');

    expect(cmp.panelVariables().map((v) => v.name).sort()).toEqual(['fatura', 'faturalar']);
  });

  it('does not inject item variables for a node outside any loop', () => {
    const fixture = TestBed.createComponent(DesignerComponent);
    const cmp = fixture.componentInstance;
    cmp.variables.set([]);
    cmp.currentGraph.set({
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [{ id: 'x', type: 'activity' }], connections: [],
    });
    cmp.selectedNodeId.set('x');
    expect(cmp.panelVariables()).toEqual([]);
  });
});
