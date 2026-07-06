import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
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
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
