import { checkStructuralInvariants } from './structural-invariants';
import { treeToWorkflow } from './tree-to-workflow';
import { step, container } from './structured-model';
import { WorkflowNode } from '../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('checkStructuralInvariants', () => {
  it('accepts a nested loop/if/tryCatch graph produced by treeToWorkflow', () => {
    const tree = [
      container('forEach', { items: '${xs}', itemVariable: 'x' }, {
        body: [
          container('if', { condition: '{{x}} == 1' }, {
            true: [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [] })],
            false: [step(n('elseStep'))],
          }),
        ],
      }),
      step(n('done')),
    ];
    let i = 0;
    const wf = treeToWorkflow(tree, { idGen: () => `c${++i}` });
    expect(checkStructuralInvariants(wf)).toEqual([]);
  });

  it('flags a loop missing its loop-back edge', () => {
    const wf = treeToWorkflow([container('while', {}, { body: [step(n('b'))] })], { idGen: () => 'L' });
    wf.connections = wf.connections.filter((c) => c.toPort !== 'loop-back');
    expect(checkStructuralInvariants(wf)).toContain('while node L: loop-back kenari yok');
  });
});
