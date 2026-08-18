import { treeToWorkflow } from './tree-to-workflow';
import { step, container } from './structured-model';
import { WorkflowNode } from '../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

function seqIds(): () => string {
  let i = 0;
  return () => `c${++i}`;
}

describe('treeToWorkflow — linear sequences', () => {
  it('emits an empty workflow for an empty tree', () => {
    const wf = treeToWorkflow([]);
    expect(wf.nodes).toEqual([]);
    expect(wf.connections).toEqual([]);
    expect(wf.schemaVersion).toBe('1.0');
  });

  it('links consecutive steps with out/in connections', () => {
    const wf = treeToWorkflow([step(n('a')), step(n('b')), step(n('c'))]);
    expect(wf.nodes.map((x) => x.id)).toEqual(['a', 'b', 'c']);
    expect(wf.connections).toEqual([
      { from: 'a', to: 'b', fromPort: 'out', toPort: 'in' },
      { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' },
    ]);
  });

  it('applies id/name/version from options', () => {
    const wf = treeToWorkflow([step(n('a'))], { id: 'w1', name: 'Demo', version: '2.0.0' });
    expect(wf).toMatchObject({ id: 'w1', name: 'Demo', version: '2.0.0' });
  });
});

describe('treeToWorkflow — loops', () => {
  it('wires body, loop-back and exit for a forEach', () => {
    const tree = [
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('bodyA'))] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    const fe = wf.nodes.find((x) => x.type === 'forEach')!;
    expect(fe.id).toBe('c1');
    expect(fe['items']).toBe('${xs}');
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'bodyA', fromPort: 'body', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'bodyA', to: 'c1', fromPort: 'out', toPort: 'loop-back' });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'after', fromPort: 'exit', toPort: 'in' });
  });
});

describe('treeToWorkflow — if', () => {
  it('wires both branches and converges to the next item', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [step(n('f'))] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 't', fromPort: 'true', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'f', fromPort: 'false', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 't', to: 'after', fromPort: 'out', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'f', to: 'after', fromPort: 'out', toPort: 'in' });
  });

  it('an empty branch flows directly to the successor via its port', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    expect(wf.connections).toContainEqual({ from: 't', to: 'after', fromPort: 'out', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'after', fromPort: 'false', toPort: 'in' });
  });
});

describe('treeToWorkflow — tryCatch', () => {
  it('stores children as node properties and continues from the finally tail', () => {
    const tree = [
      container('tryCatch', { exceptionVariable: 'ex' }, {
        success: [step(n('tryA'))],
        failure: [step(n('catchA'))],
        out: [step(n('finA'))],
      }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    const tc = wf.nodes.find((x) => x.type === 'tryCatch')!;
    expect(tc['tryNodeId']).toBe('tryA');
    expect(tc['catchNodeId']).toBe('catchA');
    expect(tc['finallyNodeId']).toBe('finA');
    expect(wf.connections.some((c) => c.from === tc.id && c.fromPort === 'success')).toBe(false);
    expect(wf.connections).toContainEqual({ from: 'finA', to: 'after', fromPort: 'out', toPort: 'in' });
  });

  it('inserts an implicit merge passthrough when finally is empty', () => {
    const tree = [
      container('tryCatch', {}, { success: [step(n('tryA'))], failure: [step(n('catchA'))], out: [] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    const tc = wf.nodes.find((x) => x.type === 'tryCatch')!;
    const mergeId = tc['finallyNodeId'] as string;
    expect(wf.nodes.find((x) => x.id === mergeId)?.type).toBe('merge');
    expect(wf.connections).toContainEqual({ from: mergeId, to: 'after', fromPort: 'out', toPort: 'in' });
  });
});
