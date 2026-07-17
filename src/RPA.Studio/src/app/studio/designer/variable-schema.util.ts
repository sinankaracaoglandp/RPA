import { WorkflowVariable } from '../../shared/models/workflow.model';
import { JsonSchemaLike } from './loop-item-schema';

export interface VariableFieldPath {
  path: string;
  type: string;
  nested: boolean;
}

function displayType(def: JsonSchemaLike): string {
  if (def.type === 'array') {
    return `liste<${def.items?.type ?? 'object'}>`;
  }
  return def.type ?? 'string';
}

/**
 * Değişken şemasındaki alanları ad + tip ile döndürür. Object kök için `deg.alan`,
 * list<object> kök için `satir.alan`. Bir alan kendisi liste ise item alanları
 * bir seviye içerlek (`nested`) olarak eklenir.
 */
export function variableFieldPaths(variable: WorkflowVariable): VariableFieldPath[] {
  const schema = variable.schema as JsonSchemaLike | undefined;
  if (!schema || typeof schema !== 'object') {
    return [];
  }
  const root = schema.type === 'array' ? schema.items : schema;
  const rootPrefix = schema.type === 'array' ? 'satir' : variable.name;
  const rows: VariableFieldPath[] = [];
  for (const [name, def] of Object.entries(root?.properties ?? {})) {
    rows.push({ path: `${rootPrefix}.${name}`, type: displayType(def), nested: false });
    if (def.type === 'array' && def.items?.properties) {
      for (const [childName, childDef] of Object.entries(def.items.properties)) {
        rows.push({ path: `satir.${childName}`, type: displayType(childDef), nested: true });
      }
    }
  }
  return rows;
}
