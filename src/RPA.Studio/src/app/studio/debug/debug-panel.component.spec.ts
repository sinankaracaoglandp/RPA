import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { Subject } from 'rxjs';
import { DebugPanelComponent } from './debug-panel.component';
import { DebugService } from '../../shared/services/debug.service';
import { RobotHubService } from '../../shared/services/robot-hub.service';
import { AuthService } from '../../auth/auth.service';
import {
  BreakpointHitEvent,
  ConnectionStatus,
  ExecutionStoppedEvent,
  JobStatusChangedEvent,
  VariableUpdatedEvent,
} from '../../shared/models/debug.model';
import { WorkflowVersion } from '../../shared/models/workflow.model';

/** Controllable fake of RobotHubService used to drive DebugService in tests. */
class FakeRobotHub {
  readonly connectionStatus = signal<ConnectionStatus>('disconnected');
  readonly jobStatusChanged$ = new Subject<JobStatusChangedEvent>();
  readonly breakpointHit$ = new Subject<BreakpointHitEvent>();
  readonly variableUpdated$ = new Subject<VariableUpdatedEvent>();
  readonly executionStopped$ = new Subject<ExecutionStoppedEvent>();

  calls: string[] = [];

  connect = vi.fn(async () => {
    this.connectionStatus.set('connected');
  });
  disconnect = vi.fn(async () => {
    this.connectionStatus.set('disconnected');
  });
  isConnected = () => this.connectionStatus() === 'connected';

  setBreakpoint = vi.fn(async (id: string) => this.calls.push(`set:${id}`));
  clearBreakpoint = vi.fn(async (id: string) => this.calls.push(`clear:${id}`));
  executeWithBreakpoints = vi.fn(async () => this.calls.push('execute'));
  resume = vi.fn(async () => this.calls.push('resume'));
  stepInto = vi.fn(async () => this.calls.push('stepInto'));
  stepOver = vi.fn(async () => this.calls.push('stepOver'));
  pause = vi.fn(async () => this.calls.push('pause'));
  stop = vi.fn(async () => this.calls.push('stop'));
}

class FakeAuth {
  roles: string[] = ['Developer'];
  getRoles = () => this.roles;
  getToken = () => 'jwt';
}

const WORKFLOW: WorkflowVersion = {
  schemaVersion: '1.0',
  id: 'wf-1',
  name: 'Test',
  version: '1.0.0',
  nodes: [{ id: 'node-a', type: 'activity', activity: 'Web.Click' }],
  connections: [],
  variables: [{ name: 'counter', type: 'int', scope: 'global', default: 0 }],
};

describe('DebugPanelComponent', () => {
  let fixture: ComponentFixture<DebugPanelComponent>;
  let component: DebugPanelComponent;
  let hub: FakeRobotHub;
  let auth: FakeAuth;
  let debug: DebugService;

  async function setup(roles: string[] = ['Developer']): Promise<void> {
    hub = new FakeRobotHub();
    auth = new FakeAuth();
    auth.roles = roles;

    await TestBed.configureTestingModule({
      imports: [DebugPanelComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        DebugService,
        { provide: RobotHubService, useValue: hub },
        { provide: AuthService, useValue: auth },
      ],
    }).compileComponents();

    debug = TestBed.inject(DebugService);
    await debug.connect();

    fixture = TestBed.createComponent(DebugPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('workflow', WORKFLOW);
    fixture.detectChanges();
  }

  it('loads the debug panel when a workflow is selected', async () => {
    await setup();
    const panel = fixture.nativeElement.querySelector('[data-testid="debug-panel"]');
    expect(panel).toBeTruthy();
    expect(panel.getAttribute('aria-label')).toBeTruthy();
  });

  it('shows the Agent connection status indicator (online)', async () => {
    await setup();
    const status = fixture.nativeElement.querySelector('[data-testid="connection-status"]');
    expect(status.textContent).toContain('debug.connection.connected');
  });

  it('displays the execution state (Idle initially)', async () => {
    await setup();
    const state = fixture.nativeElement.querySelector('[data-testid="execution-state"]');
    expect(state.textContent).toContain('debug.state.idle');
  });

  it('enables the execute button for a Developer role', async () => {
    await setup(['Developer']);
    const btn = fixture.nativeElement.querySelector('[data-testid="debug-execute"]');
    expect(btn.disabled).toBe(false);
  });

  it('disables the execute button and shows a notice for a Viewer role', async () => {
    await setup(['Viewer']);
    const btn = fixture.nativeElement.querySelector('[data-testid="debug-execute"]');
    expect(btn.disabled).toBe(true);
    const note = fixture.nativeElement.querySelector('[data-testid="debug-permission-note"]');
    expect(note).toBeTruthy();
  });

  it('starts execution with the enabled breakpoints and sets Running', async () => {
    await setup();
    debug.setBreakpoint('node-a');
    await component.onExecute();
    fixture.detectChanges();

    expect(hub.executeWithBreakpoints).toHaveBeenCalled();
    const arg = (hub.executeWithBreakpoints.mock.calls[0] as unknown[])[2];
    expect(arg).toEqual(['node-a']);
    expect(debug.executionState()).toBe('Running');
  });

  it('pauses on BreakpointHit, highlights the node and shows variables', async () => {
    await setup();
    hub.breakpointHit$.next({
      jobRunId: 'job-1',
      nodeId: 'node-a',
      variables: { counter: 5, name: 'ada' },
    });
    fixture.detectChanges();

    expect(debug.executionState()).toBe('Paused');
    expect(debug.currentNodeId()).toBe('node-a');

    const values = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="watch-value"]'),
    ).map((el) => (el as HTMLElement).textContent);
    expect(values.join(' ')).toContain('5');
    expect(values.join(' ')).toContain('ada');
  });

  it('refreshes watch variables on a VariableUpdated event', async () => {
    await setup();
    hub.variableUpdated$.next({
      jobRunId: 'job-1',
      nodeId: 'node-a',
      variables: [{ name: 'counter', value: 42, type: 'int', scope: 'global' }],
    });
    fixture.detectChanges();

    const value = fixture.nativeElement.querySelector('[data-testid="watch-value"]');
    expect(value.textContent).toContain('42');
  });

  it('emits step commands to the hub via the step controls', async () => {
    await setup();
    // Move into a paused state so controls are active.
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'node-a', variables: {} });
    fixture.detectChanges();

    component.onResume();
    component.onStepInto();
    component.onStepOver();
    await Promise.resolve();

    expect(hub.resume).toHaveBeenCalled();
    expect(hub.stepInto).toHaveBeenCalled();
    expect(hub.stepOver).toHaveBeenCalled();
  });

  it('stops execution and sets Stopped', async () => {
    await setup();
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'node-a', variables: {} });
    component.onStop();
    await new Promise((r) => setTimeout(r, 0));
    fixture.detectChanges();

    expect(hub.stop).toHaveBeenCalled();
    expect(debug.executionState()).toBe('Stopped');
  });

  it('renders breakpoints and removes one on the remove action', async () => {
    await setup();
    debug.setBreakpoint('node-a');
    fixture.detectChanges();

    let items = fixture.nativeElement.querySelectorAll('[data-testid="breakpoint-item"]');
    expect(items.length).toBe(1);

    const removeBtn = fixture.nativeElement.querySelector('[data-testid="breakpoint-remove"]');
    removeBtn.click();
    fixture.detectChanges();

    items = fixture.nativeElement.querySelectorAll('[data-testid="breakpoint-item"]');
    expect(items.length).toBe(0);
  });

  it('reflects an execution error in an alert and the Error state', async () => {
    await setup();
    hub.executionStopped$.next({ jobRunId: 'j', error: 'RFC_COMMUNICATION_FAILURE' });
    fixture.detectChanges();

    expect(debug.executionState()).toBe('Error');
    const err = fixture.nativeElement.querySelector('[data-testid="debug-error"]');
    expect(err.textContent).toContain('RFC_COMMUNICATION_FAILURE');
  });

  it('shows an offline indicator when the Agent disconnects', async () => {
    await setup();
    hub.connectionStatus.set('disconnected');
    fixture.detectChanges();

    const status = fixture.nativeElement.querySelector('[data-testid="connection-status"]');
    expect(status.textContent).toContain('debug.connection.disconnected');
  });
});
