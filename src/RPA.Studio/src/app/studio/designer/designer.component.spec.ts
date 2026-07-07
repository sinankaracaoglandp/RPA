import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
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
});
