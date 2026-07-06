import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../../auth/auth.service';
import {
  Breakpoint,
  ConnectionStatus,
  DebugVariable,
  DebugVariableScope,
  ExecutionState,
  VariableGroup,
  VariablePayload,
} from '../models/debug.model';
import { WorkflowVersion } from '../models/workflow.model';
import { RobotHubService } from './robot-hub.service';

/** Roles permitted to execute/debug workflows. Viewer is read-only. */
export const EXECUTE_ROLES = ['Developer', 'Operator', 'Admin'] as const;

const SCOPE_ORDER: DebugVariableScope[] = ['argument', 'global', 'component', 'local'];

/**
 * Orchestrates the Debug/Step-Through IDE (Faz 5, Task 5.4): local breakpoint
 * state, the execution state machine, and RobotHub command/event plumbing.
 *
 * Breakpoints are UI-local (not persisted). The state machine is driven by
 * RobotHub events (BreakpointHit → Paused, ExecutionStopped → Stopped/Error).
 */
@Injectable({ providedIn: 'root' })
export class DebugService {
  private readonly hub = inject(RobotHubService);
  private readonly auth = inject(AuthService);

  private readonly breakpointsSig = signal<Breakpoint[]>([]);
  private readonly variablesSig = signal<DebugVariable[]>([]);
  private readonly stateSig = signal<ExecutionState>('Idle');
  private readonly currentNodeSig = signal<string | null>(null);
  private readonly errorSig = signal<string | null>(null);
  private readonly jobRunIdSig = signal<string | null>(null);

  private subscriptions?: Subscription;

  readonly breakpoints: Signal<Breakpoint[]> = this.breakpointsSig.asReadonly();
  readonly variables: Signal<DebugVariable[]> = this.variablesSig.asReadonly();
  readonly executionState: Signal<ExecutionState> = this.stateSig.asReadonly();
  readonly currentNodeId: Signal<string | null> = this.currentNodeSig.asReadonly();
  readonly error: Signal<string | null> = this.errorSig.asReadonly();
  readonly jobRunId: Signal<string | null> = this.jobRunIdSig.asReadonly();

  /** RobotHub connection status, re-exposed for the online/offline indicator. */
  readonly connectionStatus: Signal<ConnectionStatus> = this.hub.connectionStatus;

  /** Variables grouped by scope for the watch-window tree. */
  readonly variableGroups = computed<VariableGroup[]>(() => {
    const groups = new Map<DebugVariableScope, DebugVariable[]>();
    for (const variable of this.variablesSig()) {
      const bucket = groups.get(variable.scope) ?? [];
      bucket.push(variable);
      groups.set(variable.scope, bucket);
    }
    return SCOPE_ORDER.filter((scope) => groups.has(scope)).map((scope) => ({
      scope,
      variables: groups.get(scope)!,
    }));
  });

  /** True when the Agent is online and execution is not in progress. */
  readonly canExecute = computed(() => {
    const roles = this.auth.getRoles();
    const roleAllowed = roles.some((r) => (EXECUTE_ROLES as readonly string[]).includes(r));
    const idle = this.stateSig() === 'Idle' || this.stateSig() === 'Stopped' || this.stateSig() === 'Error';
    return roleAllowed && this.hub.isConnected() && idle;
  });

  /** True when step controls (resume/step/pause/stop) are meaningful. */
  readonly canControl = computed(() => {
    const state = this.stateSig();
    return this.hub.isConnected() && (state === 'Running' || state === 'Paused');
  });

  readonly isPaused = computed(() => this.stateSig() === 'Paused');
  readonly isRunning = computed(() => this.stateSig() === 'Running');

  /** Whether the current user's role allows executing/debugging at all. */
  hasExecutePermission(): boolean {
    return this.auth.getRoles().some((r) => (EXECUTE_ROLES as readonly string[]).includes(r));
  }

  /** Connects to RobotHub and subscribes to debug events. Idempotent. */
  async connect(): Promise<void> {
    this.subscribeToHub();
    await this.hub.connect();
  }

  async disconnect(): Promise<void> {
    this.subscriptions?.unsubscribe();
    this.subscriptions = undefined;
    await this.hub.disconnect();
  }

  // --- breakpoints ---------------------------------------------------------

  /** Toggles a breakpoint on a node (set if absent, clear if present). */
  toggleBreakpoint(nodeId: string): void {
    const existing = this.breakpointsSig().find((b) => b.nodeId === nodeId);
    if (existing) {
      this.clearBreakpoint(nodeId);
    } else {
      this.setBreakpoint(nodeId);
    }
  }

  setBreakpoint(nodeId: string): void {
    if (this.breakpointsSig().some((b) => b.nodeId === nodeId)) {
      return;
    }
    this.breakpointsSig.update((list) => [...list, { nodeId, enabled: true }]);
    if (this.hub.isConnected() && this.jobRunIdSig()) {
      void this.hub.setBreakpoint(nodeId, this.jobRunIdSig() ?? undefined).catch(() => undefined);
    }
  }

  clearBreakpoint(nodeId: string): void {
    if (!this.breakpointsSig().some((b) => b.nodeId === nodeId)) {
      return;
    }
    this.breakpointsSig.update((list) => list.filter((b) => b.nodeId !== nodeId));
    if (this.hub.isConnected() && this.jobRunIdSig()) {
      void this.hub.clearBreakpoint(nodeId, this.jobRunIdSig() ?? undefined).catch(() => undefined);
    }
  }

  /** Enables/disables a breakpoint without removing it. */
  setBreakpointEnabled(nodeId: string, enabled: boolean): void {
    this.breakpointsSig.update((list) =>
      list.map((b) => (b.nodeId === nodeId ? { ...b, enabled } : b)),
    );
  }

  hasBreakpoint(nodeId: string): boolean {
    return this.breakpointsSig().some((b) => b.nodeId === nodeId);
  }

  clearAllBreakpoints(): void {
    this.breakpointsSig.set([]);
  }

  // --- execution -----------------------------------------------------------

  /** Starts execution on the Agent with the currently-enabled breakpoints. */
  async execute(workflow: WorkflowVersion, args: Record<string, unknown> = {}): Promise<void> {
    if (!this.hasExecutePermission()) {
      throw new Error('Insufficient role to execute workflows');
    }
    this.errorSig.set(null);
    this.currentNodeSig.set(null);
    this.variablesSig.set(this.argumentVariables(workflow, args));
    this.stateSig.set('Running');

    const enabled = this.breakpointsSig()
      .filter((b) => b.enabled)
      .map((b) => b.nodeId);
    try {
      await this.hub.executeWithBreakpoints(JSON.stringify(workflow), args, enabled);
    } catch (err) {
      this.stateSig.set('Error');
      this.errorSig.set(err instanceof Error ? err.message : String(err));
      throw err;
    }
  }

  async resume(): Promise<void> {
    await this.sendControl(() => this.hub.resume(), 'Running');
  }

  async stepInto(): Promise<void> {
    await this.sendControl(() => this.hub.stepInto(), 'Running');
  }

  async stepOver(): Promise<void> {
    await this.sendControl(() => this.hub.stepOver(), 'Running');
  }

  async pause(): Promise<void> {
    await this.sendControl(() => this.hub.pause(), 'Running');
  }

  async stop(): Promise<void> {
    await this.hub.stop().catch((err) => {
      this.errorSig.set(err instanceof Error ? err.message : String(err));
    });
    this.stateSig.set('Stopped');
    this.currentNodeSig.set(null);
  }

  /** Resets debug state back to Idle (e.g. when switching workflows). */
  reset(): void {
    this.stateSig.set('Idle');
    this.variablesSig.set([]);
    this.currentNodeSig.set(null);
    this.errorSig.set(null);
    this.jobRunIdSig.set(null);
  }

  // --- internals -----------------------------------------------------------

  private async sendControl(action: () => Promise<unknown>, nextState: ExecutionState): Promise<void> {
    try {
      await action();
      this.stateSig.set(nextState);
    } catch (err) {
      this.stateSig.set('Error');
      this.errorSig.set(err instanceof Error ? err.message : String(err));
      throw err;
    }
  }

  private subscribeToHub(): void {
    if (this.subscriptions) {
      return;
    }
    const sub = new Subscription();
    sub.add(
      this.hub.breakpointHit$.subscribe((evt) => {
        this.jobRunIdSig.set(evt.jobRunId);
        this.currentNodeSig.set(evt.nodeId);
        this.variablesSig.set(this.normalizeVariables(evt.variables));
        this.stateSig.set('Paused');
      }),
    );
    sub.add(
      this.hub.variableUpdated$.subscribe((evt) => {
        this.jobRunIdSig.set(evt.jobRunId);
        this.currentNodeSig.set(evt.nodeId);
        this.variablesSig.set(this.normalizeVariables(evt.variables));
      }),
    );
    sub.add(
      this.hub.executionStopped$.subscribe((evt) => {
        this.currentNodeSig.set(null);
        if (evt.error) {
          this.stateSig.set('Error');
          this.errorSig.set(evt.error);
        } else {
          this.stateSig.set('Stopped');
        }
      }),
    );
    sub.add(
      this.hub.jobStatusChanged$.subscribe((evt) => {
        this.jobRunIdSig.set(evt.jobRunId);
        const mapped = this.mapJobStatus(evt.status);
        if (mapped) {
          this.stateSig.set(mapped);
        }
      }),
    );
    this.subscriptions = sub;
  }

  private mapJobStatus(status: string): ExecutionState | null {
    switch (status.toLowerCase()) {
      case 'running':
      case 'inprogress':
        return 'Running';
      case 'paused':
        return 'Paused';
      case 'completed':
      case 'succeeded':
      case 'stopped':
        return 'Stopped';
      case 'failed':
      case 'faulted':
      case 'error':
        return 'Error';
      default:
        return null;
    }
  }

  private argumentVariables(
    workflow: WorkflowVersion,
    args: Record<string, unknown>,
  ): DebugVariable[] {
    const declared = (workflow.variables ?? []).map<DebugVariable>((v) => ({
      name: v.name,
      value: args[v.name] ?? v.default ?? null,
      type: v.type,
      scope: (v.scope as DebugVariableScope) ?? 'global',
    }));
    const declaredNames = new Set(declared.map((v) => v.name));
    const extras = Object.entries(args)
      .filter(([name]) => !declaredNames.has(name))
      .map<DebugVariable>(([name, value]) => ({ name, value, scope: 'argument' }));
    return [...extras, ...declared];
  }

  private normalizeVariables(payload: VariablePayload): DebugVariable[] {
    if (Array.isArray(payload)) {
      return payload.map((v) => ({
        name: v.name,
        value: v.value,
        type: v.type,
        scope: v.scope ?? 'local',
      }));
    }
    return Object.entries(payload ?? {}).map(([name, value]) => ({
      name,
      value,
      scope: 'local' as const,
    }));
  }
}
