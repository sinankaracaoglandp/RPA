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

  it('rejects a tryCatch graph with a clear reason', () => {
    const wf = treeToWorkflow([container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })], { idGen: seqIds() });
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toContain('tryCatch');
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
