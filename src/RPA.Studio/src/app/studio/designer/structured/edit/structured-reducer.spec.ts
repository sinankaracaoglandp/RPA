import { reduceWorkflow } from './structured-reducer';
import { treeToWorkflow } from '../tree-to-workflow';
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
