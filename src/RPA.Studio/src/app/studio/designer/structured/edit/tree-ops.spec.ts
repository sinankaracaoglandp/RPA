import {
  insertItem, removeItem, moveItem, findPath, newStep, newContainer,
  findSeqPath, reorderInSeq, moveAcross, updateItemAt, setItemProps, setItemLabel, duplicateItem,
} from './tree-ops';
import { step, container, StructuredSequence, StepItem, ContainerItem } from '../structured-model';
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

describe('tree-ops — props editing', () => {
  it('setItemProps replaces a step node properties (immutable)', () => {
    const tree: StructuredSequence = [step(n('a'))];
    const out = setItemProps(tree, { steps: [], index: 0 }, { message: 'hi' });
    expect((out[0] as { node: { properties: unknown } }).node.properties).toEqual({ message: 'hi' });
    expect((tree[0] as { node: { properties?: unknown } }).node.properties).toBeUndefined();
  });

  it('setItemProps replaces a container props', () => {
    const tree: StructuredSequence = [container('forEach', { items: '${a}' }, { body: [] })];
    const out = setItemProps(tree, { steps: [], index: 0 }, { items: '${b}', itemVariable: 'x' });
    expect((out[0] as { props: unknown }).props).toEqual({ items: '${b}', itemVariable: 'x' });
  });

  it('updateItemAt transforms the addressed item', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b'))];
    const out = updateItemAt(tree, { steps: [], index: 1 },
      (it) => ({ ...(it as { kind: 'step'; node: WorkflowNode }), node: { ...(it as { node: WorkflowNode }).node, id: 'B2' } } as never));
    expect((out[1] as { node: WorkflowNode }).node.id).toBe('B2');
  });
});

describe('setItemLabel', () => {
  it('writes the label onto a step node and clears it when blank', () => {
    const tree = [step({ id: 'a', type: 'activity', activity: 'A' })];
    const named = setItemLabel(tree, { steps: [], index: 0 }, '  Fatura no girişi  ');
    expect((named[0] as StepItem).node.label).toBe('Fatura no girişi');
    const cleared = setItemLabel(named, { steps: [], index: 0 }, '   ');
    expect((cleared[0] as StepItem).node.label).toBeUndefined();
  });

  it('writes the label onto container props', () => {
    const tree = [container('forEach', { items: '${a}' }, { body: [] })];
    const named = setItemLabel(tree, { steps: [], index: 0 }, 'Faturaları gez');
    expect((named[0] as ContainerItem).props['label']).toBe('Faturaları gez');
  });

  it('keeps the container label when the properties panel writes props back', () => {
    const tree = setItemLabel(
      [container('forEach', { items: '${a}' }, { body: [] })], { steps: [], index: 0 }, 'Faturaları gez',
    );
    const edited = setItemProps(tree, { steps: [], index: 0 }, { items: '${b}' });
    expect((edited[0] as ContainerItem).props['label']).toBe('Faturaları gez');
  });
});

describe('duplicateItem', () => {
  let seq = 0;
  const ids = () => `copy-${++seq}`;
  beforeEach(() => { seq = 0; });

  it('inserts the copy right after the original with a fresh node id', () => {
    const tree = [step({ id: 'a', type: 'activity', activity: 'A' }), step(n('z'))];
    const r = duplicateItem(tree, { steps: [], index: 0 }, ids)!;
    expect(r.tree).toHaveLength(3);
    expect((r.tree[1] as StepItem).node.id).toBe('copy-1');
    expect((r.tree[1] as StepItem).node.activity).toBe('A');
    expect((r.tree[2] as StepItem).node.id).toBe('z');
  });

  it('deep-copies properties so editing the copy does not touch the original', () => {
    const tree = [step({
      id: 'a', type: 'activity', activity: 'Desktop.SendKeys',
      properties: { keys: JSON.stringify([{ type: 'Text', text: 'x' }]), selector: '/Window' },
    })];
    const r = duplicateItem(tree, { steps: [], index: 0 }, ids)!;
    const copy = (r.tree[1] as StepItem).node;
    (copy.properties as Record<string, unknown>)['selector'] = '/Other';
    expect(((r.tree[0] as StepItem).node.properties as Record<string, unknown>)['selector']).toBe('/Window');
  });

  it('copies a container with all lane children, each child getting a fresh id', () => {
    const tree = [container('forEach', { items: '${xs}' }, {
      body: [step({ id: 'b1', type: 'activity', activity: 'A' }), step({ id: 'b2', type: 'activity', activity: 'B' })],
    })];
    const r = duplicateItem(tree, { steps: [], index: 0 }, ids)!;
    const copy = r.tree[1] as ContainerItem;
    expect(copy.props['items']).toBe('${xs}');
    expect((copy.lanes.body as StructuredSequence).map((c) => (c as StepItem).node.id))
      .toEqual(['copy-1', 'copy-2']);
  });

  it('duplicates an item nested inside a lane in place', () => {
    const tree = [container('forEach', { items: '${xs}' }, { body: [step(n('b1'))] })];
    const r = duplicateItem(tree, { steps: [{ lane: 'body', index: 0 }], index: 0 }, ids)!;
    const body = (r.tree[0] as ContainerItem).lanes.body as StructuredSequence;
    expect(body.map((c) => (c as StepItem).node.id)).toEqual(['b1', 'copy-1']);
  });
});
