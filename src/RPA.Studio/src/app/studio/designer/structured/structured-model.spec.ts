import { step, container, lanesFor } from './structured-model';
import { WorkflowNode } from '../../../shared/models/workflow.model';

describe('structured-model', () => {
  it('wraps a workflow node as a step item', () => {
    const node: WorkflowNode = { id: 'n1', type: 'activity', activity: 'Web.Click' };
    expect(step(node)).toEqual({ kind: 'step', node });
  });

  it('builds a container item with props and lanes', () => {
    const c = container('forEach', { items: '${x}' }, { body: [] });
    expect(c.kind).toBe('container');
    expect(c.type).toBe('forEach');
    expect(c.props).toEqual({ items: '${x}' });
    expect(c.lanes.body).toEqual([]);
  });

  it('lists valid lanes per container type', () => {
    expect(lanesFor('while')).toEqual(['body']);
    expect(lanesFor('if')).toEqual(['true', 'false']);
    expect(lanesFor('tryCatch')).toEqual(['success', 'failure', 'out']);
  });
});
