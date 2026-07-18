import { reduceWorkflow } from './structured-reducer';
import { treeToWorkflow } from '../tree-to-workflow';
import { workflowToTree } from '../workflow-to-tree';
import { step, container, StructuredSequence } from '../structured-model';
import { WorkflowNode, WorkflowVersion } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });
const seqIds = () => { let i = 0; return () => `c${++i}`; };
const ok = (r: { ok: boolean }) => {
  if (!r.ok) { throw new Error('beklenen ok:true, reason: ' + (r as { reason?: string }).reason); }
  return r as { ok: true; tree: StructuredSequence };
};

describe('reduceWorkflow — reducible', () => {
  it('reduces a linear sequence', () => {
    const wf = treeToWorkflow([step(n('a')), step(n('b'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect(r.tree.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['a', 'b']);
  });

  it('reduces a forEach loop', () => {
    const wf = treeToWorkflow([container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] }), step(n('after'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect(r.tree).toHaveLength(2);
    expect((r.tree[0] as { type: string }).type).toBe('forEach');
  });

  it('treats a Logic.ForEach activity node as an (empty-body) forEach container', () => {
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [
        { id: 'fe', type: 'activity', activity: 'Logic.ForEach', items: '${xs}', itemVariable: 'x' } as WorkflowNode,
        { id: 'after', type: 'activity', activity: 'A' },
      ],
      connections: [{ from: 'fe', to: 'after', fromPort: 'out', toPort: 'in' }],
    };
    const r = ok(reduceWorkflow(wf));
    expect(r.tree).toHaveLength(2);
    const fe = r.tree[0] as { kind: string; type: string; props: Record<string, unknown>; lanes: Record<string, unknown[]> };
    expect(fe.kind).toBe('container');
    expect(fe.type).toBe('forEach');
    expect(fe.props).toEqual({ items: '${xs}', itemVariable: 'x' }); // 'activity'/'type' props'a sizmaz
    expect(fe.lanes['body']).toEqual([]);
    expect((r.tree[1] as { node: WorkflowNode }).node.id).toBe('after');
  });

  it('treats a Logic.If activity node as an (empty-branch) if container', () => {
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [{ id: 'iff', type: 'activity', activity: 'Logic.If', condition: '{{c}}' } as WorkflowNode],
      connections: [],
    };
    const r = ok(reduceWorkflow(wf));
    const it = r.tree[0] as { kind: string; type: string };
    expect(it.kind).toBe('container');
    expect(it.type).toBe('if');
  });

  it('reduces a simple if with converging branches', () => {
    const wf = treeToWorkflow([container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [step(n('f'))] }), step(n('after'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect((r.tree[0] as { type: string }).type).toBe('if');
    expect((r.tree[1] as { node: WorkflowNode }).node.id).toBe('after');
  });
});

describe('reduceWorkflow — rejected', () => {
  it('rejects multiple entry nodes', () => {
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [n('a'), n('b'), n('c')],
      connections: [{ from: 'a', to: 'c', fromPort: 'out', toPort: 'in' }, { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' }],
    };
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toContain('giriş');
  });

});

describe('reduceWorkflow — tryCatch (D2)', () => {
  it('reduces a tryCatch and folds the continuation into finally', () => {
    const wf = treeToWorkflow([
      container('tryCatch', { exceptionVariable: 'ex' }, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] }),
      step(n('after')),
    ], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect(r.tree).toHaveLength(1); // tryCatch terminal
    const tc = r.tree[0] as { type: string; props: Record<string, unknown>; lanes: { success: { node: WorkflowNode }[]; failure: { node: WorkflowNode }[]; out: { node: WorkflowNode }[] } };
    expect(tc.type).toBe('tryCatch');
    expect(tc.props).toEqual({ exceptionVariable: 'ex' });
    expect(tc.lanes.success.map((i) => i.node.id)).toEqual(['t']);
    expect(tc.lanes.failure.map((i) => i.node.id)).toEqual(['c']);
    expect(tc.lanes.out.map((i) => i.node.id)).toEqual(['fin', 'after']); // devam finally'ye katlandı
  });

  it('reduces a tryCatch with an empty finally (merge stripped)', () => {
    const wf = treeToWorkflow([container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [] })], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    const tc = r.tree[0] as { type: string; lanes: { out: unknown[] } };
    expect(tc.type).toBe('tryCatch');
    expect(tc.lanes.out).toHaveLength(0);
  });

  it('reduces a tryCatch nested inside an if branch', () => {
    const wf = treeToWorkflow([
      container('if', {}, {
        true: [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('f'))] })],
        false: [step(n('e'))],
      }),
      step(n('after')),
    ], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    const iff = r.tree[0] as { type: string; lanes: { true: { type: string }[] } };
    expect(iff.type).toBe('if');
    expect(iff.lanes.true[0].type).toBe('tryCatch');
  });
});

describe('reduceWorkflow — if edge cases', () => {
  it('reduces an if with an empty false branch', () => {
    const wf = treeToWorkflow([container('if', {}, { true: [step(n('t'))], false: [] }), step(n('after'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    const iff = r.tree[0] as { type: string; lanes: { true: unknown[]; false: unknown[] } };
    expect(iff.type).toBe('if');
    expect(iff.lanes.false).toHaveLength(0);
  });

  it('reduces a nested loop inside an if branch', () => {
    const wf = treeToWorkflow([
      container('if', {}, {
        true: [container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] })],
        false: [step(n('f'))],
      }),
      step(n('after')),
    ], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    const iff = r.tree[0] as { lanes: { true: { type: string }[] } };
    expect(iff.lanes.true[0].type).toBe('forEach');
  });

  it('rejects a non-branch fork (step with multiple outgoing edges)', () => {
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [n('a'), n('b'), n('c')],
      connections: [
        { from: 'a', to: 'b', fromPort: 'out', toPort: 'in' },
        { from: 'a', to: 'c', fromPort: 'out', toPort: 'in' },
      ],
    };
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toContain('çıkış');
  });

  it('rejects a cross-branch leak into the middle of the other branch', () => {
    // if1 true→t1→t2→conv ; false→f1→conv ; t1→f1 (dallar arası sızıntı, f1 iki daldan ulaşılıyor)
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [{ id: 'if1', type: 'if' }, n('t1'), n('t2'), n('f1'), n('conv')],
      connections: [
        { from: 'if1', to: 't1', fromPort: 'true', toPort: 'in' },
        { from: 'if1', to: 'f1', fromPort: 'false', toPort: 'in' },
        { from: 't1', to: 't2', fromPort: 'out', toPort: 'in' },
        { from: 't1', to: 'f1', fromPort: 'out', toPort: 'in' },
        { from: 't2', to: 'conv', fromPort: 'out', toPort: 'in' },
        { from: 'f1', to: 'conv', fromPort: 'out', toPort: 'in' },
      ],
    };
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toMatch(/çıkış|ulaşıl|sızın|yakınsama|atlıyor/);
  });
});

describe('reduceWorkflow — A round-trip agreement', () => {
  it('agrees with workflowToTree on structured-authored graphs', () => {
    const trees: StructuredSequence[] = [
      [step(n('a')), step(n('b'))],
      [container('while', { condition: '{{c}}' }, { body: [step(n('x'))] }), step(n('y'))],
      [container('if', {}, { true: [step(n('t'))], false: [step(n('f'))] }), step(n('z'))],
    ];
    for (const t of trees) {
      const wf = treeToWorkflow(t, { idGen: seqIds() });
      const viaReduce = ok(reduceWorkflow(wf)).tree;
      const viaTree = workflowToTree(wf);
      expect(viaReduce.map((i) => i.kind)).toEqual(viaTree.map((i) => i.kind));
    }
  });
});
