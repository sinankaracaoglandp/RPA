import { WorkflowNode, WorkflowVariable, WorkflowVersion } from '../../shared/models/workflow.model';

export interface JsonSchemaLike {
  type?: string;
  properties?: Record<string, JsonSchemaLike>;
  items?: JsonSchemaLike;
}

export interface LoopItemField {
  name: string;
  type: string;
}

const VALID_NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;
const SINGLE_TOKEN = /^\s*(?:\$\{|\{\{)\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\}\}|\})\s*$/;

/** `${ad}` / `{{ad}}` biçiminden kök değişken adını çıkarır; karmaşık ifadede null. */
export function parseRootVariableName(expr: string | undefined): string | null {
  if (!expr) {
    return null;
  }
  const m = SINGLE_TOKEN.exec(expr);
  return m ? m[1] : null;
}

/** Bir list<object> değişkeninin eleman (item) şemasını döndürür; yoksa null. */
function elementSchemaOf(variable: WorkflowVariable | undefined): JsonSchemaLike | null {
  const schema = variable?.schema as JsonSchemaLike | undefined;
  if (!schema || typeof schema !== 'object' || schema.type !== 'array' || !schema.items) {
    return null;
  }
  return schema.items;
}

/** Elle tanımlı alanlardan bir object şeması kurar. */
function schemaFromFields(fields: LoopItemField[]): JsonSchemaLike {
  const properties: Record<string, JsonSchemaLike> = {};
  for (const f of fields) {
    if (f?.name) {
      properties[f.name] = { type: f.type || 'string' };
    }
  }
  return { type: 'object', properties };
}

/**
 * ForEach node'unun eleman değişkenini türetir. Önce `items` bağlı bir list<object>
 * değişkeninin eleman şemasını dener; bulunamazsa node'daki elle `itemFields`'e düşer.
 * Geçersiz itemVariable adında null döner.
 */
export function deriveLoopItemVariable(
  forEachNode: WorkflowNode,
  variables: WorkflowVariable[],
): WorkflowVariable | null {
  const name = (forEachNode['itemVariable'] as string) || 'item';
  if (!VALID_NAME.test(name)) {
    return null;
  }
  const rootName = parseRootVariableName(forEachNode['items'] as string | undefined);
  const source = rootName ? variables.find((v) => v.name === rootName) : undefined;
  const derived = elementSchemaOf(source);
  const fields = (forEachNode['itemFields'] as LoopItemField[] | undefined) ?? [];
  const schema = derived ?? (fields.length > 0 ? schemaFromFields(fields) : null);
  if (!schema) {
    return null;
  }
  return { name, type: 'object', schema };
}

/** ForEach'in body portundan başlayıp loop-back'e kadar ulaşılan gövde node id kümesi. */
function bodyNodeIds(forEachId: string, graph: WorkflowVersion): Set<string> {
  const start = graph.connections.find(
    (c) => c.from === forEachId && c.fromPort === 'body',
  )?.to;
  const body = new Set<string>();
  if (!start) {
    return body;
  }
  const queue = [start];
  while (queue.length > 0) {
    const id = queue.shift()!;
    if (id === forEachId || body.has(id)) {
      continue;
    }
    body.add(id);
    for (const c of graph.connections) {
      // loop-back kenarı gövdeyi ForEach'e geri bağlar; onu izlemeyiz (döngüyü kapatır).
      if (c.from === id && c.toPort !== 'loop-back') {
        queue.push(c.to);
      }
    }
  }
  return body;
}

/** Verilen node'u saran tüm ForEach node'ları (iç içe döngülerde birden çok). */
export function enclosingForEachNodes(nodeId: string, graph: WorkflowVersion): WorkflowNode[] {
  return graph.nodes.filter(
    (n) => n.type === 'forEach' && bodyNodeIds(n.id, graph).has(nodeId),
  );
}

/**
 * Seçili node'un içinde bulunduğu ForEach döngüleri için türetilen item değişkenleri.
 * Kalıcıya yazılmaz; yalnız properties panelinin autocomplete listesine eklenmek içindir.
 */
export function injectedLoopVariables(
  nodeId: string | null,
  graph: WorkflowVersion,
  variables: WorkflowVariable[],
): WorkflowVariable[] {
  if (!nodeId) {
    return [];
  }
  const result: WorkflowVariable[] = [];
  for (const fe of enclosingForEachNodes(nodeId, graph)) {
    const item = deriveLoopItemVariable(fe, variables);
    if (item) {
      result.push(item);
    }
  }
  return result;
}
