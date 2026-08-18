import { workflowToTree } from './workflow-to-tree';
import { treeToWorkflow } from './tree-to-workflow';
import { step, container, StructuredSequence } from './structured-model';
import { WorkflowNode } from '../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });
let counter = 0;
const ids = () => `c${++counter}`;
beforeEach(() => { counter = 0; });

function roundTrip(tree: StructuredSequence): StructuredSequence {
  return workflowToTree(treeToWorkflow(tree, { idGen: ids }));
}

describe('workflowToTree — round-trip', () => {
  it('round-trips a linear sequence', () => {
    const tree = [step(n('a')), step(n('b'))];
    expect(roundTrip(tree)).toEqual(tree);
  });

  it('round-trips a forEach with a body', () => {
    const tree = [
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('bodyA'))] }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    expect(back[0].kind).toBe('container');
    const c = back[0] as { type: string; lanes: { body: unknown[] } };
    expect(c.type).toBe('forEach');
    expect(c.lanes.body).toHaveLength(1);
  });

  it('round-trips an if with converging branches', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [step(n('f'))] }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    expect((back[0] as { type: string }).type).toBe('if');
    expect((back[1] as { node: WorkflowNode }).node.id).toBe('after');
  });

  it('round-trips an if with an empty false branch', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [] }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    const iff = back[0] as { type: string; lanes: { true: unknown[]; false: unknown[] } };
    expect(iff.type).toBe('if');
    expect(iff.lanes.true).toHaveLength(1);
    expect(iff.lanes.false).toHaveLength(0);
  });

  it('round-trips a nested loop inside an if branch', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, {
        true: [container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] })],
        false: [step(n('f'))],
      }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    const iff = back[0] as { type: string; lanes: { true: unknown[] } };
    expect(iff.type).toBe('if');
    expect((iff.lanes.true[0] as { type: string }).type).toBe('forEach');
  });

  it('throws for tryCatch reverse (Faz-A kapsamı dışı)', () => {
    const wf = treeToWorkflow(
      [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
      { idGen: ids },
    );
    expect(() => workflowToTree(wf)).toThrow(/tryCatch ters/);
  });
});
