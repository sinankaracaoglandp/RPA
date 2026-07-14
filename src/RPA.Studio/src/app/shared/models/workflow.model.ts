/**
 * Studio-side representation of the Workflow JSON (Spec Bölüm 5.1,
 * src/RPA.Domain/WorkflowSchema.json v1.0).
 *
 * Note: `position` is a UI-only field for canvas layout. The domain schema
 * does not declare it but permits it (no `additionalProperties: false`),
 * so persisting it is non-breaking for the BaseRunner.
 */

export type WorkflowNodeType =
  | 'activity'
  | 'assign'
  | 'if'
  | 'forEach'
  | 'for'
  | 'while'
  | 'tryCatch'
  | 'userPrompt'
  | 'delay'
  | 'log'
  | 'checkpoint'
  | 'terminate'
  | 'componentCall'
  | 'merge';

export type ConnectionPort = 'out' | 'success' | 'failure' | 'true' | 'false' | 'body' | 'exit';
export type ConnectionTargetPort = 'in' | 'loop-back';

export interface NodePosition {
  x: number;
  y: number;
}

export interface WorkflowNode {
  id: string;
  type: WorkflowNodeType;
  /** Activity id (e.g. 'Sap.Nco.CallBapi') — required when type === 'activity'. */
  activity?: string;
  channel?: 'nco' | 'gui';
  properties?: Record<string, unknown>;
  /** UI-only canvas coordinate. */
  position?: NodePosition;
  [key: string]: unknown;
}

export interface WorkflowConnection {
  from: string;
  fromPort?: ConnectionPort;
  toPort?: ConnectionTargetPort;
  to: string;
  label?: string;
}

export interface WorkflowVariable {
  name: string;
  type: string;
  scope?: 'local' | 'component' | 'global';
  default?: unknown;
  description?: string;
}

export interface WorkflowVersion {
  schemaVersion: string;
  id: string;
  name: string;
  version: string;
  nodes: WorkflowNode[];
  connections: WorkflowConnection[];
  variables?: WorkflowVariable[];
}

export function emptyWorkflow(id: string = crypto.randomUUID(), name: string = 'Untitled'): WorkflowVersion {
  return {
    schemaVersion: '1.0',
    id,
    name,
    version: '1.0.0',
    nodes: [],
    connections: [],
    variables: [],
  };
}
