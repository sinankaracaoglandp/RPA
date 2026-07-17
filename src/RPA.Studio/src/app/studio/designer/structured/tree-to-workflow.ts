import {
  ConnectionPort, WorkflowConnection, WorkflowNode, WorkflowVersion,
} from '../../../shared/models/workflow.model';
import { ContainerItem, StructuredItem, StructuredSequence } from './structured-model';

export interface OpenTail {
  nodeId: string;
  fromPort: ConnectionPort;
}

export interface EmitResult {
  head: string | null;
  tails: OpenTail[];
}

export interface TreeToWorkflowOptions {
  id?: string;
  name?: string;
  version?: string;
  idGen?: () => string;
}

interface Builder {
  nodes: WorkflowNode[];
  connections: WorkflowConnection[];
  idGen: () => string;
}

export function treeToWorkflow(
  tree: StructuredSequence,
  opts: TreeToWorkflowOptions = {},
): WorkflowVersion {
  const builder: Builder = {
    nodes: [],
    connections: [],
    idGen: opts.idGen ?? (() => crypto.randomUUID()),
  };
  emitSequence(tree, builder);
  return {
    schemaVersion: '1.0',
    id: opts.id ?? crypto.randomUUID(),
    name: opts.name ?? 'Untitled',
    version: opts.version ?? '1.0.0',
    nodes: builder.nodes,
    connections: builder.connections,
  };
}

/** Diziyi yayar; ardışık öğeleri bağlar. Head = ilk öğe head'i; tails = son öğe tail'leri. */
function emitSequence(seq: StructuredSequence, b: Builder): EmitResult {
  let head: string | null = null;
  let prevTails: OpenTail[] = [];
  for (const item of seq) {
    const r = emitItem(item, b);
    if (r.head === null) {
      continue;
    }
    if (head === null) {
      head = r.head;
    }
    for (const t of prevTails) {
      b.connections.push({ from: t.nodeId, to: r.head, fromPort: t.fromPort, toPort: 'in' });
    }
    prevTails = r.tails;
  }
  return { head, tails: prevTails };
}

function emitItem(item: StructuredItem, b: Builder): EmitResult {
  if (item.kind === 'step') {
    b.nodes.push(item.node);
    return { head: item.node.id, tails: [{ nodeId: item.node.id, fromPort: 'out' }] };
  }
  return emitContainer(item, b);
}

function emitContainer(item: ContainerItem, b: Builder): EmitResult {
  const id = b.idGen();
  const node: WorkflowNode = { id, type: item.type, ...item.props };
  b.nodes.push(node);

  if (item.type === 'forEach' || item.type === 'for' || item.type === 'while') {
    const body = emitSequence(item.lanes.body ?? [], b);
    if (body.head !== null) {
      b.connections.push({ from: id, to: body.head, fromPort: 'body', toPort: 'in' });
      for (const t of body.tails) {
        b.connections.push({ from: t.nodeId, to: id, fromPort: t.fromPort, toPort: 'loop-back' });
      }
    }
    return { head: id, tails: [{ nodeId: id, fromPort: 'exit' }] };
  }

  if (item.type === 'if') {
    const tails: OpenTail[] = [];
    for (const [lane, port] of [['true', 'true'], ['false', 'false']] as const) {
      const r = emitSequence(item.lanes[lane] ?? [], b);
      if (r.head !== null) {
        b.connections.push({ from: id, to: r.head, fromPort: port, toPort: 'in' });
        tails.push(...r.tails);
      } else {
        // Boş dal: if node'un o portu doğrudan ardıla akar.
        tails.push({ nodeId: id, fromPort: port });
      }
    }
    return { head: id, tails };
  }

  // tryCatch
  const writable = node as Record<string, unknown>;
  const success = emitSequence(item.lanes.success ?? [], b);
  const failure = emitSequence(item.lanes.failure ?? [], b);
  let out = emitSequence(item.lanes.out ?? [], b);

  if (success.head !== null) { writable['tryNodeId'] = success.head; }
  if (failure.head !== null) { writable['catchNodeId'] = failure.head; }

  // Finally boşsa: devam noktası için tek-node'luk örtük 'merge' geçişi ekle.
  if (out.head === null) {
    const mergeId = b.idGen();
    b.nodes.push({ id: mergeId, type: 'merge' });
    out = { head: mergeId, tails: [{ nodeId: mergeId, fromPort: 'out' }] };
  }
  writable['finallyNodeId'] = out.head!;

  return { head: id, tails: out.tails };
}
