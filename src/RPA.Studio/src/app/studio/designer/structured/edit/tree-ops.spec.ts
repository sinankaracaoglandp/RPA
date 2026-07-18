import {
  insertItem, removeItem, moveItem, findPath, newStep, newContainer,
  findSeqPath, reorderInSeq, moveAcross,
} from './tree-ops';
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

describe('tree-ops — drag-drop helpers', () => {
  it('findSeqPath locates the root and a nested lane by reference', () => {
    const body = [step(n('x'))];
    const tree: StructuredSequence = [container('forEach', {}, { body })];
    expect(findSeqPath(tree, tree)).toEqual([]);
    expect(findSeqPath(tree, body)).toEqual([{ lane: 'body', index: 0 }]);
    expect(findSeqPath(tree, [step(n('nope'))])).toBeNull();
  });

  it('reorderInSeq moves within a sequence (moveItemInArray semantics)', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b')), step(n('c'))];
    const out = reorderInSeq(tree, [], 0, 2);
    expect(out.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['b', 'c', 'a']);
  });

  it('moveAcross moves an item between two lanes', () => {
    const tree: StructuredSequence = [
      container('if', {}, { true: [step(n('t0'))], false: [step(n('f0'))] }),
    ];
    const out = moveAcross(tree, [{ lane: 'true', index: 0 }], 0, [{ lane: 'false', index: 0 }], 1);
    const c = out[0] as { lanes: { true: unknown[]; false: { node: WorkflowNode }[] } };
    expect(c.lanes.true).toHaveLength(0);
    expect(c.lanes.false.map((i) => i.node.id)).toEqual(['f0', 't0']);
  });

  it('moveAcross into an ancestor sequence (out of a lane to the root) stays correct', () => {
    const inner = step(n('inner'));
    const tree: StructuredSequence = [
      step(n('a')),
      container('forEach', {}, { body: [inner] }),
    ];
    const out = moveAcross(tree, [{ lane: 'body', index: 1 }], 0, [], 2);
    expect((out[0] as { node: WorkflowNode }).node.id).toBe('a');
    expect((out[1] as { lanes: { body: unknown[] } }).lanes.body).toHaveLength(0);
    expect((out[2] as { node: WorkflowNode }).node.id).toBe('inner');
  });

  it('moveAcross adjusts a target path that passes through the source after the removed index', () => {
    const tree: StructuredSequence = [
      step(n('a')),
      container('if', {}, { true: [], false: [] }),
    ];
    const out = moveAcross(tree, [], 0, [{ lane: 'true', index: 1 }], 0);
    expect(out).toHaveLength(1);
    const c = out[0] as { type: string; lanes: { true: { node: WorkflowNode }[] } };
    expect(c.type).toBe('if');
    expect(c.lanes.true.map((i) => i.node.id)).toEqual(['a']);
  });
});
