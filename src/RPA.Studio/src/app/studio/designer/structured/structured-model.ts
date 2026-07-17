import { WorkflowNode } from '../../../shared/models/workflow.model';

export type ContainerType = 'forEach' | 'for' | 'while' | 'if' | 'tryCatch';
export type LaneName = 'body' | 'true' | 'false' | 'success' | 'failure' | 'out';

export interface StepItem {
  kind: 'step';
  node: WorkflowNode;
}

export interface ContainerItem {
  kind: 'container';
  type: ContainerType;
  props: Record<string, unknown>;
  lanes: Partial<Record<LaneName, StructuredSequence>>;
}

export type StructuredItem = StepItem | ContainerItem;
export type StructuredSequence = StructuredItem[];

export function step(node: WorkflowNode): StepItem {
  return { kind: 'step', node };
}

export function container(
  type: ContainerType,
  props: Record<string, unknown>,
  lanes: Partial<Record<LaneName, StructuredSequence>>,
): ContainerItem {
  return { kind: 'container', type, props, lanes };
}

/** Konteyner tipinin geçerli lane adları (mevcut port adlarıyla birebir). */
export function lanesFor(type: ContainerType): LaneName[] {
  switch (type) {
    case 'if':
      return ['true', 'false'];
    case 'tryCatch':
      return ['success', 'failure', 'out'];
    default:
      return ['body'];
  }
}
