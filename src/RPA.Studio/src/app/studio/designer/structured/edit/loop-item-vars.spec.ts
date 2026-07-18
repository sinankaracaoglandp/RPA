import { enclosingLoopItemVars } from './loop-item-vars';
import { step, container, StructuredSequence } from '../structured-model';
import { WorkflowNode, WorkflowVariable } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('enclosingLoopItemVars', () => {
  const listVar: WorkflowVariable = {
    name: 'faturalar', type: 'list<object>',
    schema: { type: 'array', items: { type: 'object', properties: { tutar: { type: 'number' } } } },
  };

  it('derives the item variable for a node inside a forEach body', () => {
    const inner = step(n('inner'));
    const tree: StructuredSequence = [
      container('forEach', { items: '${faturalar}', itemVariable: 'fatura' }, { body: [inner] }),
    ];
    const vars = enclosingLoopItemVars(tree, inner, [listVar]);
    expect(vars.map((v) => v.name)).toEqual(['fatura']);
    expect(vars[0].schema).toEqual({ type: 'object', properties: { tutar: { type: 'number' } } });
  });

  it('returns empty for a node outside any loop', () => {
    const s = step(n('a'));
    expect(enclosingLoopItemVars([s], s, [])).toEqual([]);
  });

  it('derives nested loop items (loop inside loop)', () => {
    const inner = step(n('inner'));
    const tree: StructuredSequence = [
      container('forEach', { items: '${faturalar}', itemVariable: 'fatura' }, {
        body: [container('for', { start: 0, end: 3, indexVariable: 'i' }, { body: [inner] })],
      }),
    ];
    const vars = enclosingLoopItemVars(tree, inner, [listVar]);
    expect(vars.map((v) => v.name)).toContain('fatura');
  });
});
