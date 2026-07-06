import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Subject } from 'rxjs';
import { DebugService } from './debug.service';
import { RobotHubService } from './robot-hub.service';
import { AuthService } from '../../auth/auth.service';
import {
  BreakpointHitEvent,
  ConnectionStatus,
  ExecutionStoppedEvent,
  JobStatusChangedEvent,
  VariableUpdatedEvent,
} from '../models/debug.model';
import { WorkflowVersion } from '../models/workflow.model';

class FakeHub {
  readonly connectionStatus = signal<ConnectionStatus>('disconnected');
  readonly jobStatusChanged$ = new Subject<JobStatusChangedEvent>();
  readonly breakpointHit$ = new Subject<BreakpointHitEvent>();
  readonly variableUpdated$ = new Subject<VariableUpdatedEvent>();
  readonly executionStopped$ = new Subject<ExecutionStoppedEvent>();
  isConnected = () => this.connectionStatus() === 'connected';
  connect = vi.fn(async () => this.connectionStatus.set('connected'));
  disconnect = vi.fn(async () => this.connectionStatus.set('disconnected'));
  setBreakpoint = vi.fn(async () => undefined);
  clearBreakpoint = vi.fn(async () => undefined);
  executeWithBreakpoints = vi.fn(async () => undefined);
  resume = vi.fn(async () => undefined);
  stepInto = vi.fn(async () => undefined);
  stepOver = vi.fn(async () => undefined);
  pause = vi.fn(async () => undefined);
  stop = vi.fn(async () => undefined);
}

class FakeAuth {
  roles: string[] = ['Developer'];
  getRoles = () => this.roles;
  getToken = () => 'jwt';
}

const WF: WorkflowVersion = {
  schemaVersion: '1.0',
  id: 'wf',
  name: 'wf',
  version: '1.0.0',
  nodes: [{ id: 'n1', type: 'activity' }],
  connections: [],
  variables: [{ name: 'x', type: 'int', scope: 'global', default: 1 }],
};

function make(roles = ['Developer']): { svc: DebugService; hub: FakeHub; auth: FakeAuth } {
  const hub = new FakeHub();
  const auth = new FakeAuth();
  auth.roles = roles;
  TestBed.configureTestingModule({
    providers: [
      DebugService,
      { provide: RobotHubService, useValue: hub },
      { provide: AuthService, useValue: auth },
    ],
  });
  return { svc: TestBed.inject(DebugService), hub, auth };
}

describe('DebugService', () => {
  it('toggles a breakpoint on and off', () => {
    const { svc } = make();
    svc.toggleBreakpoint('n1');
    expect(svc.hasBreakpoint('n1')).toBe(true);
    svc.toggleBreakpoint('n1');
    expect(svc.hasBreakpoint('n1')).toBe(false);
  });

  it('does not add duplicate breakpoints and can enable/disable', () => {
    const { svc } = make();
    svc.setBreakpoint('n1');
    svc.setBreakpoint('n1');
    expect(svc.breakpoints().length).toBe(1);
    svc.setBreakpointEnabled('n1', false);
    expect(svc.breakpoints()[0].enabled).toBe(false);
    svc.clearAllBreakpoints();
    expect(svc.breakpoints().length).toBe(0);
  });

  it('rejects execute when the role lacks permission', async () => {
    const { svc } = make(['Viewer']);
    await expect(svc.execute(WF)).rejects.toThrow('Insufficient role');
    expect(svc.hasExecutePermission()).toBe(false);
  });

  it('seeds argument variables and sets Running on execute', async () => {
    const { svc, hub } = make();
    await svc.connect();
    await svc.execute(WF, { y: 9 });
    expect(hub.executeWithBreakpoints).toHaveBeenCalled();
    expect(svc.executionState()).toBe('Running');
    const names = svc.variables().map((v) => v.name);
    expect(names).toContain('x');
    expect(names).toContain('y');
  });

  it('transitions Running → Paused on BreakpointHit with variables', async () => {
    const { svc, hub } = make();
    await svc.connect();
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'n1', variables: { a: 1 } });
    expect(svc.executionState()).toBe('Paused');
    expect(svc.currentNodeId()).toBe('n1');
    expect(svc.variableGroups().length).toBeGreaterThan(0);
  });

  it('resumes back to Running after a pause', async () => {
    const { svc, hub } = make();
    await svc.connect();
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'n1', variables: {} });
    await svc.resume();
    expect(hub.resume).toHaveBeenCalled();
    expect(svc.executionState()).toBe('Running');
  });

  it('maps job status changes to execution states', async () => {
    const { svc, hub } = make();
    await svc.connect();
    hub.jobStatusChanged$.next({ jobRunId: 'j', status: 'Running' });
    expect(svc.executionState()).toBe('Running');
    hub.jobStatusChanged$.next({ jobRunId: 'j', status: 'Completed' });
    expect(svc.executionState()).toBe('Stopped');
  });

  it('goes to Error with a message on ExecutionStopped error', async () => {
    const { svc, hub } = make();
    await svc.connect();
    hub.executionStopped$.next({ jobRunId: 'j', error: 'boom' });
    expect(svc.executionState()).toBe('Error');
    expect(svc.error()).toBe('boom');
  });

  it('stops and resets state', async () => {
    const { svc, hub } = make();
    await svc.connect();
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'n1', variables: {} });
    await svc.stop();
    expect(hub.stop).toHaveBeenCalled();
    expect(svc.executionState()).toBe('Stopped');
    svc.reset();
    expect(svc.executionState()).toBe('Idle');
    expect(svc.variables().length).toBe(0);
  });

  it('sends step-over and pause commands', async () => {
    const { svc, hub } = make();
    await svc.connect();
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'n1', variables: {} });
    await svc.stepOver();
    expect(hub.stepOver).toHaveBeenCalled();
    hub.breakpointHit$.next({ jobRunId: 'j', nodeId: 'n1', variables: {} });
    await svc.pause();
    expect(hub.pause).toHaveBeenCalled();
  });
});
