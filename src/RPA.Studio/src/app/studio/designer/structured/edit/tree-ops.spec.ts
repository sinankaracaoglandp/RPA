import { insertItem, removeItem, moveItem, findPath, newStep, newContainer } from './tree-ops';
import { step, container, StructuredSequence } from '../structured-model';
import { WorkflowNode } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('tree-ops', () => {
  it('inserts into the root sequence at an index', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b'))];
    const out = insertItem(tree, [], 1, step(n('mid')));
    expect(out.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['a', 'mid', 'b']);
    expect(tree).toHaveLength(2); // immutable
  });

  it('inserts into a container lane', () => {
    const tree: StructuredSequence = [container('forEach', {}, { body: [step(n('x'))] })];
    const out = insertItem(tree, [{ lane: 'body', index: 0 }], 1, step(n('y')));
    const body = (out[0] as { lanes: { body: { node: WorkflowNode }[] } }).lanes.body;
    expect(body.map((i) => i.node.id)).toEqual(['x', 'y']);
  });

  it('removes an item by path', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b'))];
    const out = removeItem(tree, { steps: [], index: 0 });
    expect(out.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['b']);
  });

  it('moves an item within its sequence and is a no-op at bounds', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b')), step(n('c'))];
    const down = moveItem(tree, { steps: [], index: 0 }, 1);
    expect(down.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['b', 'a', 'c']);
    const noop = moveItem(tree, { steps: [], index: 0 }, -1);
    expect(noop.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['a', 'b', 'c']);
  });

  it('finds the path of an item by reference (nested)', () => {
    const inner = step(n('inner'));
    const tree: StructuredSequence = [container('if', {}, { true: [inner], false: [] })];
    expect(findPath(tree, inner)).toEqual({ steps: [{ lane: 'true', index: 0 }], index: 0 });
    expect(findPath(tree, step(n('nope')))).toBeNull();
  });

  it('newStep/newContainer produce well-formed items', () => {
    const s = newStep('Web.Click');
    expect(s.kind).toBe('step');
    expect((s.node as WorkflowNode).activity).toBe('Web.Click');
    const c = newContainer('tryCatch');
    expect(c.kind).toBe('container');
    expect(Object.keys(c.lanes).sort()).toEqual(['failure', 'out', 'success']);
    expect(c.lanes.success).toEqual([]);
  });
});
