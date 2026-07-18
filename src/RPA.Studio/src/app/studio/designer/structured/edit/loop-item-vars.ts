import { WorkflowNode, WorkflowVariable } from '../../../../shared/models/workflow.model';
import { StructuredItem, StructuredSequence } from '../structured-model';
import { findPath } from './tree-ops';
import { deriveLoopItemVariable } from '../../loop-item-schema';

/**
 * Yapısal ağaçta bir öğeyi saran ForEach/For döngülerinin `item` değişkenlerini
 * (ağaç-yolundan) türetir. Serbest-graf'taki graf-tabanlı enjeksiyonun yapısal-mod karşılığı.
 */
export function enclosingLoopItemVars(
  tree: StructuredSequence,
  item: StructuredItem,
  variables: WorkflowVariable[],
): WorkflowVariable[] {
  const path = findPath(tree, item);
  if (!path) { return []; }
  const result: WorkflowVariable[] = [];
  let seq = tree;
  for (const stepp of path.steps) {
    const c = seq[stepp.index];
    if (c.kind === 'container') {
      if ((c.type === 'forEach' || c.type === 'for') && stepp.lane === 'body') {
        const node = { id: 'loop', type: c.type, ...c.props } as unknown as WorkflowNode;
        const v = deriveLoopItemVariable(node, variables);
        if (v) { result.push(v); }
      }
      seq = c.lanes[stepp.lane] ?? [];
    } else {
      break;
    }
  }
  return result;
}
