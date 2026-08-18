import {
  parseRootVariableName,
  deriveLoopItemVariable,
  enclosingForEachNodes,
  injectedLoopVariables,
} from './loop-item-schema';
import { WorkflowVariable, WorkflowVersion, WorkflowNode } from '../../shared/models/workflow.model';

const listVar: WorkflowVariable = {
  name: 'faturalar',
  type: 'list<object>',
  schema: {
    type: 'array',
    items: { type: 'object', properties: { tutar: { type: 'number' }, musteri: { type: 'string' } } },
  },
};

function graph(nodes: WorkflowNode[], connections: WorkflowVersion['connections']): WorkflowVersion {
  return { schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0', nodes, connections };
}

describe('parseRootVariableName', () => {
  it('extracts name from ${...} and {{...}}', () => {
    expect(parseRootVariableName('${faturalar}')).toBe('faturalar');
    expect(parseRootVariableName('{{ faturalar }}')).toBe('faturalar');
  });
  it('returns null for complex or empty expressions', () => {
    expect(parseRootVariableName('${a.b[0]}')).toBeNull();
    expect(parseRootVariableName('')).toBeNull();
    expect(parseRootVariableName(undefined)).toBeNull();
  });
});

describe('deriveLoopItemVariable', () => {
  it('derives element schema from a bound list<object> variable', () => {
    const fe: WorkflowNode = { id: 'fe', type: 'forEach', items: '${faturalar}', itemVariable: 'fatura' };
    const v = deriveLoopItemVariable(fe, [listVar]);
    expect(v).toEqual({
      name: 'fatura',
      type: 'object',
      schema: { type: 'object', properties: { tutar: { type: 'number' }, musteri: { type: 'string' } } },
    });
  });
  it('defaults itemVariable name to "item"', () => {
    const fe: WorkflowNode = { id: 'fe', type: 'forEach', items: '${faturalar}' };
    expect(deriveLoopItemVariable(fe, [listVar])?.name).toBe('item');
  });
  it('falls back to manual itemFields when source has no schema', () => {
    const fe: WorkflowNode = {
      id: 'fe', type: 'forEach', items: '${hamListe}', itemVariable: 'satir',
      itemFields: [{ name: 'id', type: 'string' }, { name: 'adet', type: 'int' }],
    };
    expect(deriveLoopItemVariable(fe, [])).toEqual({
      name: 'satir',
      type: 'object',
      schema: { type: 'object', properties: { id: { type: 'string' }, adet: { type: 'int' } } },
    });
  });
  it('rejects an invalid itemVariable name', () => {
    const fe: WorkflowNode = { id: 'fe', type: 'forEach', items: '${faturalar}', itemVariable: '1bad' };
    expect(deriveLoopItemVariable(fe, [listVar])).toBeNull();
  });
});

describe('enclosingForEachNodes', () => {
  it('finds the loop enclosing a body node, excluding exit-side nodes', () => {
    const nodes: WorkflowNode[] = [
      { id: 'fe', type: 'forEach', items: '${faturalar}', itemVariable: 'fatura' },
      { id: 'a', type: 'activity' },
      { id: 'b', type: 'activity' },
      { id: 'after', type: 'activity' },
    ];
    const conns = [
      { from: 'fe', to: 'a', fromPort: 'body' as const },
      { from: 'a', to: 'b' },
      { from: 'b', to: 'fe', toPort: 'loop-back' as const },
      { from: 'fe', to: 'after', fromPort: 'exit' as const },
    ];
    expect(enclosingForEachNodes('a', graph(nodes, conns)).map((n) => n.id)).toEqual(['fe']);
    expect(enclosingForEachNodes('b', graph(nodes, conns)).map((n) => n.id)).toEqual(['fe']);
    expect(enclosingForEachNodes('after', graph(nodes, conns))).toEqual([]);
  });
});

describe('injectedLoopVariables', () => {
  it('returns the item variable for a node inside the loop body', () => {
    const nodes: WorkflowNode[] = [
      { id: 'fe', type: 'forEach', items: '${faturalar}', itemVariable: 'fatura' },
      { id: 'a', type: 'activity' },
    ];
    const conns = [
      { from: 'fe', to: 'a', fromPort: 'body' as const },
      { from: 'a', to: 'fe', toPort: 'loop-back' as const },
    ];
    const injected = injectedLoopVariables('a', graph(nodes, conns), [listVar]);
    expect(injected.map((v) => v.name)).toEqual(['fatura']);
  });
  it('returns empty for a null selection or node outside any loop', () => {
    expect(injectedLoopVariables(null, graph([], []), [])).toEqual([]);
  });
});
