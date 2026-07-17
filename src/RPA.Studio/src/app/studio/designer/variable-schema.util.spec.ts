import { variableFieldPaths } from './variable-schema.util';
import { WorkflowVariable } from '../../shared/models/workflow.model';

describe('variableFieldPaths', () => {
  it('uses "satir" prefix for a list<object> root', () => {
    const v: WorkflowVariable = {
      name: 'faturalar', type: 'list<object>',
      schema: { type: 'array', items: { type: 'object', properties: { tutar: { type: 'number' } } } },
    };
    expect(variableFieldPaths(v)).toEqual([{ path: 'satir.tutar', type: 'number', nested: false }]);
  });
  it('uses the variable name for an object root', () => {
    const v: WorkflowVariable = {
      name: 'fatura', type: 'object',
      schema: { type: 'object', properties: { musteri: { type: 'string' } } },
    };
    expect(variableFieldPaths(v)).toEqual([{ path: 'fatura.musteri', type: 'string', nested: false }]);
  });
  it('returns [] when there is no schema', () => {
    expect(variableFieldPaths({ name: 'x', type: 'string' })).toEqual([]);
  });
});
