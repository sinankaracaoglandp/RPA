/**
 * Debug/Step-Through IDE domain types (Faz 5, Task 5.4).
 *
 * These describe the runtime-inspection state the Studio holds while an Agent
 * executes a workflow over the RobotHub SignalR channel: breakpoints, watched
 * variables and the execution state machine.
 */

/** Execution state machine (spec Bölüm 5.2 — Running/Paused/Stopped semantics). */
export type ExecutionState = 'Idle' | 'Running' | 'Paused' | 'Stopped' | 'Error';

/** RobotHub connection lifecycle, surfaced to the Agent online/offline indicator. */
export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** A UI-local breakpoint. Not persisted to the database (brief §Important Notes). */
export interface Breakpoint {
  nodeId: string;
  enabled: boolean;
}

/** A single watched variable value at a point in execution. */
export interface DebugVariable {
  name: string;
  value: unknown;
  type?: string;
  scope: DebugVariableScope;
}

export type DebugVariableScope = 'global' | 'component' | 'local' | 'argument';

/** Watch-window tree grouping — variables organised by scope. */
export interface VariableGroup {
  scope: DebugVariableScope;
  variables: DebugVariable[];
}

/**
 * Raw variable payload as delivered by the Agent. Accepts either a keyed map
 * ({ name: value }) or an explicit array of descriptors; DebugService
 * normalises both into DebugVariable[].
 */
export type VariablePayload =
  | Record<string, unknown>
  | Array<{ name: string; value: unknown; type?: string; scope?: DebugVariableScope }>;

/** RobotHub → Studio event payloads. */
export interface BreakpointHitEvent {
  jobRunId: string;
  nodeId: string;
  variables: VariablePayload;
}

export interface VariableUpdatedEvent {
  jobRunId: string;
  nodeId: string;
  variables: VariablePayload;
}

export interface ExecutionStoppedEvent {
  jobRunId: string;
  error?: string | null;
}

export interface JobStatusChangedEvent {
  jobRunId: string;
  status: string;
}
