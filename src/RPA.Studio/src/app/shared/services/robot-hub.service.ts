import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { AuthService } from '../../auth/auth.service';
import {
  BreakpointHitEvent,
  ConnectionStatus,
  ExecutionStoppedEvent,
  JobStatusChangedEvent,
  VariableUpdatedEvent,
} from '../models/debug.model';

/**
 * Minimal surface of a SignalR HubConnection that RobotHubService depends on.
 * Abstracted so unit tests can inject a fake connection without a live socket.
 */
export interface RobotHubConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, handler: (...args: unknown[]) => void): void;
  off(methodName: string): void;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  onclose(callback: (error?: Error) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
}

export type RobotHubConnectionFactory = (
  url: string,
  tokenProvider: () => string | null,
) => RobotHubConnection;

/** RobotHub endpoint (Faz 3, Task 3.1). WebSocket, JWT in the query string. */
export const ROBOT_HUB_URL = '/hubs/robot';

/**
 * Default factory: builds a real @microsoft/signalr HubConnection with JWT auth.
 * Overridable in tests via the ROBOT_HUB_CONNECTION_FACTORY token.
 */
export const defaultRobotHubConnectionFactory: RobotHubConnectionFactory = (url, tokenProvider) =>
  new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory: () => tokenProvider() ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build() as unknown as RobotHubConnection;

export const ROBOT_HUB_CONNECTION_FACTORY = new InjectionToken<RobotHubConnectionFactory>(
  'ROBOT_HUB_CONNECTION_FACTORY',
  { providedIn: 'root', factory: () => defaultRobotHubConnectionFactory },
);

/** Client → Agent method names (RobotHub contract, brief §Interfaces). */
const HubMethod = {
  SetBreakpoint: 'SetBreakpoint',
  ClearBreakpoint: 'ClearBreakpoint',
  ExecuteWithBreakpoints: 'ExecuteWithBreakpoints',
  Resume: 'Resume',
  StepInto: 'StepInto',
  StepOver: 'StepOver',
  Pause: 'Pause',
  Stop: 'Stop',
} as const;

/** Agent → Client event names. */
const HubEvent = {
  JobStatusChanged: 'JobStatusChanged',
  BreakpointHit: 'BreakpointHit',
  VariableUpdated: 'VariableUpdated',
  ExecutionStopped: 'ExecutionStopped',
} as const;

/**
 * Thin wrapper over the RobotHub SignalR connection (Faz 3). Exposes observables
 * for Agent → Studio events and typed methods for Studio → Agent commands, plus a
 * reactive connection-status signal for the online/offline indicator.
 */
@Injectable({ providedIn: 'root' })
export class RobotHubService {
  private readonly auth = inject(AuthService);
  private readonly connectionFactory = inject(ROBOT_HUB_CONNECTION_FACTORY);

  private connection?: RobotHubConnection;

  readonly connectionStatus = signal<ConnectionStatus>('disconnected');

  readonly jobStatusChanged$ = new Subject<JobStatusChangedEvent>();
  readonly breakpointHit$ = new Subject<BreakpointHitEvent>();
  readonly variableUpdated$ = new Subject<VariableUpdatedEvent>();
  readonly executionStopped$ = new Subject<ExecutionStoppedEvent>();

  /** Establishes the RobotHub connection and wires event handlers (idempotent). */
  async connect(): Promise<void> {
    if (this.connection) {
      return;
    }
    this.connectionStatus.set('connecting');
    const connection = this.connectionFactory(ROBOT_HUB_URL, () => this.auth.getToken());
    this.connection = connection;
    this.registerHandlers(connection);

    try {
      await connection.start();
      this.connectionStatus.set('connected');
    } catch (error) {
      this.connectionStatus.set('disconnected');
      this.connection = undefined;
      throw error;
    }
  }

  /** Tears down the connection and marks the Agent offline. */
  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    this.connectionStatus.set('disconnected');
    if (connection) {
      await connection.stop();
    }
  }

  isConnected(): boolean {
    return this.connectionStatus() === 'connected';
  }

  // --- Studio → Agent commands ---------------------------------------------

  setBreakpoint(nodeId: string, jobRunId?: string): Promise<unknown> {
    return this.invoke(HubMethod.SetBreakpoint, nodeId, jobRunId);
  }

  clearBreakpoint(nodeId: string, jobRunId?: string): Promise<unknown> {
    return this.invoke(HubMethod.ClearBreakpoint, nodeId, jobRunId);
  }

  executeWithBreakpoints(
    workflowJson: string,
    args: Record<string, unknown>,
    breakpointNodeIds: string[],
  ): Promise<unknown> {
    return this.invoke(HubMethod.ExecuteWithBreakpoints, workflowJson, args, breakpointNodeIds);
  }

  resume(): Promise<unknown> {
    return this.invoke(HubMethod.Resume);
  }

  stepInto(): Promise<unknown> {
    return this.invoke(HubMethod.StepInto);
  }

  stepOver(): Promise<unknown> {
    return this.invoke(HubMethod.StepOver);
  }

  pause(): Promise<unknown> {
    return this.invoke(HubMethod.Pause);
  }

  stop(): Promise<unknown> {
    return this.invoke(HubMethod.Stop);
  }

  // --- internals -----------------------------------------------------------

  private invoke(method: string, ...args: unknown[]): Promise<unknown> {
    if (!this.connection || !this.isConnected()) {
      return Promise.reject(new Error('RobotHub is not connected'));
    }
    // Drop trailing undefined optional args so the hub receives a clean arity.
    const cleaned = [...args];
    while (cleaned.length && cleaned[cleaned.length - 1] === undefined) {
      cleaned.pop();
    }
    return this.connection.invoke(method, ...cleaned);
  }

  private registerHandlers(connection: RobotHubConnection): void {
    connection.on(HubEvent.JobStatusChanged, (jobRunId, status) => {
      this.jobStatusChanged$.next({ jobRunId: String(jobRunId), status: String(status) });
    });
    connection.on(HubEvent.BreakpointHit, (jobRunId, nodeId, variables) => {
      this.breakpointHit$.next({
        jobRunId: String(jobRunId),
        nodeId: String(nodeId),
        variables: (variables ?? {}) as BreakpointHitEvent['variables'],
      });
    });
    connection.on(HubEvent.VariableUpdated, (jobRunId, nodeId, variables) => {
      this.variableUpdated$.next({
        jobRunId: String(jobRunId),
        nodeId: String(nodeId),
        variables: (variables ?? {}) as VariableUpdatedEvent['variables'],
      });
    });
    connection.on(HubEvent.ExecutionStopped, (jobRunId, error) => {
      this.executionStopped$.next({
        jobRunId: String(jobRunId),
        error: (error as string | null) ?? null,
      });
    });

    connection.onreconnecting(() => this.connectionStatus.set('reconnecting'));
    connection.onreconnected(() => this.connectionStatus.set('connected'));
    connection.onclose(() => {
      this.connectionStatus.set('disconnected');
      this.connection = undefined;
    });
  }
}

/** Re-exported for callers that need to test connection state literals. */
export { HubConnectionState };
