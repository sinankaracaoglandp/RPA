# ForEach `item` Şema Türetme & Döngü Görsel Netliği — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Logic.ForEach` döngüsündeki eleman değişkeninin (`item`) alan şemasını tasarım-zamanında türetip yalnızca döngü gövdesindeki node'ların autocomplete'ine `${item.alan}` olarak sunmak; ayrıca döngü gövdesi/çıkışını canvas'ta hafifçe görselleştirmek.

**Architecture:** Tümü Angular Studio tarafında. Saf yardımcı fonksiyonlar (şema türetme + body-node tespiti) bir modülde toplanır ve `DesignerComponent` içinde seçili node için sentetik `item` `WorkflowVariable`'ı üretip **properties paneline geçen** değişken listesine eklenir (kalıcı workflow'a yazılmaz). Alan-yolu autocomplete'i paylaşılan bir util ile hem `expression-input` hem `generic-property` önerilerine eklenir. Görsel vurgu mevcut `breakpointNodeIds` deseniyle canvas'a bir `bodyHighlightNodeIds` girişi olarak bağlanır. Runtime, `WorkflowSchema.json` ve `BaseRunner` **değişmez**.

**Tech Stack:** Angular (standalone components, signals, zoneless CD), TypeScript, Jest/Jasmine (mevcut `*.spec.ts` deseni), Rete.js canvas.

## Global Constraints

- Runtime davranışı, `src/RPA.Domain/WorkflowSchema.json` ve `RPA.Infrastructure/Workflow/BaseRunner.cs` **değiştirilmez**. Bu bir tasarım-zamanı (Studio) özelliğidir.
- Enjekte edilen `item` değişkeni **kalıcı workflow JSON'ına veya Variables paneline yazılmaz**; yalnız properties paneline geçen listeye eklenir.
- `itemVariable` ad doğrulama regex'i, mevcut emsal (`designer.component.ts` e-fatura) ile aynı: `^[A-Za-z_][A-Za-z0-9_]*$`.
- Değişken adı çözümü ifade biçimleri: `${ad}` ve `{{ad}}` (yalnız tek, düz değişken referansı otomatik türetilir).
- Yeni saf fonksiyonlar dıştan bağımlılıksız ve saf tutulur (test edilebilirlik). Angular bileşenlerine iş mantığı gömülmez.
- Kapsam yalnız `Logic.ForEach`. `Logic.For` / `Logic.While` için şema türetme YOK.
- Kod stili: çevre dosyalarla aynı (TR yorumlar, mevcut adlandırma). Yeni dosyalar tek sorumluluk taşır.

---

## Dosya Yapısı

- **Create:** `src/app/studio/designer/loop-item-schema.ts` — saf fonksiyonlar: değişken-adı ayrıştırma, eleman şeması türetme, body-node tespiti, enjekte edilecek değişkenleri üretme.
- **Create:** `src/app/studio/designer/loop-item-schema.spec.ts` — yukarıdakinin birim testleri.
- **Create:** `src/app/studio/designer/variable-schema.util.ts` — bir `WorkflowVariable`'ın şemasından alan yollarını (`ad.alan`, `satir.alan`) üreten paylaşılan saf fonksiyon.
- **Create:** `src/app/studio/designer/variable-schema.util.spec.ts` — birim testleri.
- **Modify:** `src/app/studio/designer/variables/variables-panel.component.ts` — kendi alan-yolu türetmesini paylaşılan util'e devreder (davranış aynı).
- **Modify:** `src/app/studio/designer/designer.component.ts` — seçili node için enjekte edilmiş değişkenleri hesaplayan `panelVariables` computed'ı + görsel vurgu için `loopBodyHighlightIds` computed'ı.
- **Modify:** `src/app/studio/designer/designer.component.html` — properties paneline `panelVariables()` geçir; canvas'a `bodyHighlightNodeIds` bağla.
- **Modify:** `src/app/studio/designer/properties/expression-input.component.ts` — öneri üretimine alan yollarını ekle (paylaşılan util).
- **Modify:** `src/app/studio/designer/properties/generic-property.component.ts` — `expressionSuggestions` alan yollarını da önersin (paylaşılan util) + ForEach `itemVariable` ad doğrulama + fallback alan editörü desteği.
- **Modify:** `src/app/studio/designer/properties/generic-property.component.html` — ForEach için fallback alan editörü + `itemVariable` doğrulama mesajı.
- **Modify:** `src/app/studio/designer/canvas/canvas.component.ts` — `bodyHighlightNodeIds` girişi + `toView` alanı; port etiketlerini netleştir (`getOutputPorts`/`getInputPorts`); ForEach `itemFields` serialize/extract.
- **Modify:** `src/app/studio/designer/canvas/node.component.ts` — `CanvasNodeView.bodyHighlight` alanı.
- **Modify:** `src/app/studio/designer/canvas/node.component.html` — `bodyHighlight` CSS sınıfı.
- **Modify:** `src/app/studio/designer/canvas/node.component.scss` — vurgu stili.

---

### Task 1: Saf yardımcılar — değişken adı ayrıştırma, eleman şeması, body-node tespiti, enjeksiyon

**Files:**
- Create: `src/app/studio/designer/loop-item-schema.ts`
- Test: `src/app/studio/designer/loop-item-schema.spec.ts`

**Interfaces:**
- Consumes: `WorkflowVariable`, `WorkflowVersion`, `WorkflowNode` from `../../shared/models/workflow.model`.
- Produces:
  - `parseRootVariableName(expr: string | undefined): string | null`
  - `JsonSchemaLike` (type export: `{ type?: string; properties?: Record<string, JsonSchemaLike>; items?: JsonSchemaLike }`)
  - `LoopItemField` (type export: `{ name: string; type: string }`)
  - `deriveLoopItemVariable(forEachNode: WorkflowNode, variables: WorkflowVariable[]): WorkflowVariable | null`
  - `enclosingForEachNodes(nodeId: string, graph: WorkflowVersion): WorkflowNode[]`
  - `injectedLoopVariables(nodeId: string | null, graph: WorkflowVersion, variables: WorkflowVariable[]): WorkflowVariable[]`

- [ ] **Step 1: Write the failing test**

```typescript
// src/app/studio/designer/loop-item-schema.spec.ts
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest loop-item-schema`
Expected: FAIL — `Cannot find module './loop-item-schema'`.

- [ ] **Step 3: Write minimal implementation**

```typescript
// src/app/studio/designer/loop-item-schema.ts
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx jest loop-item-schema`
Expected: PASS (all describe blocks green).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/loop-item-schema.ts src/RPA.Studio/src/app/studio/designer/loop-item-schema.spec.ts
git commit -m "feat(studio): ForEach item sema turetme + body-node tespiti saf yardimcilari

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: Paylaşılan alan-yolu util'i + variables-panel devri

**Files:**
- Create: `src/app/studio/designer/variable-schema.util.ts`
- Test: `src/app/studio/designer/variable-schema.util.spec.ts`
- Modify: `src/app/studio/designer/variables/variables-panel.component.ts`

**Interfaces:**
- Consumes: `WorkflowVariable`, `JsonSchemaLike` (Task 1'den re-export edilir).
- Produces: `variableFieldPaths(variable: WorkflowVariable): Array<{ path: string; type: string; nested: boolean }>`

- [ ] **Step 1: Write the failing test**

```typescript
// src/app/studio/designer/variable-schema.util.spec.ts
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest variable-schema.util`
Expected: FAIL — module not found.

- [ ] **Step 3: Write minimal implementation**

```typescript
// src/app/studio/designer/variable-schema.util.ts
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx jest variable-schema.util`
Expected: PASS.

- [ ] **Step 5: Refactor variables-panel to use the shared util**

`src/app/studio/designer/variables/variables-panel.component.ts` içinde `schemaFieldRows` gövdesini paylaşılan util'e devret (davranış aynı; mevcut `SchemaFieldRow` dönüş tipi korunur):

```typescript
// dosya başına import ekle:
import { variableFieldPaths } from '../variable-schema.util';

// schemaFieldRows metodunu şununla değiştir:
schemaFieldRows(variable: WorkflowVariable): SchemaFieldRow[] {
  return variableFieldPaths(variable).map((r) => ({ path: r.path, type: r.type, nested: r.nested }));
}
```

`private displayType(...)` metodu artık yalnız `schemaFieldRows` tarafından kullanılıyorsa ve başka kullanan kalmadıysa kaldır. (Kalan kullanıcı varsa dokunma.)

- [ ] **Step 6: Run existing variables-panel tests + new util test**

Run: `cd src/RPA.Studio && npx jest variables-panel variable-schema.util`
Expected: PASS (mevcut variables-panel testleri kırılmadan geçer).

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/variable-schema.util.ts src/RPA.Studio/src/app/studio/designer/variable-schema.util.spec.ts src/RPA.Studio/src/app/studio/designer/variables/variables-panel.component.ts
git commit -m "refactor(studio): degisken alan-yolu turetmesini paylasilan util'e cikar

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Seçili node için item değişkenlerini properties paneline enjekte et

**Files:**
- Modify: `src/app/studio/designer/designer.component.ts`
- Modify: `src/app/studio/designer/designer.component.html:122-128`
- Test: `src/app/studio/designer/designer.component.spec.ts`

**Interfaces:**
- Consumes: `injectedLoopVariables` (Task 1).
- Produces: `DesignerComponent.panelVariables: Signal<WorkflowVariable[]>` (base variables + enjekte item değişkenleri).

- [ ] **Step 1: Write the failing test**

`src/app/studio/designer/designer.component.spec.ts` içine ekle (mevcut TestBed kurulumunu kullan; dosyanın başındaki yardımcılara uy):

```typescript
it('injects the loop item variable into panelVariables for a body node', () => {
  // Arrange: bir forEach + gövde node'u olan grafı ve list<object> degiskeni kur.
  const fixture = TestBed.createComponent(DesignerComponent);
  const cmp = fixture.componentInstance;
  cmp.variables.set([{
    name: 'faturalar', type: 'list<object>',
    schema: { type: 'array', items: { type: 'object', properties: { tutar: { type: 'number' } } } },
  }]);
  cmp.currentGraph.set({
    schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
    nodes: [
      { id: 'fe', type: 'forEach', items: '${faturalar}', itemVariable: 'fatura' },
      { id: 'a', type: 'activity' },
    ],
    connections: [
      { from: 'fe', to: 'a', fromPort: 'body' },
      { from: 'a', to: 'fe', toPort: 'loop-back' },
    ],
  });

  // Act: gövde node'unu seç.
  cmp.selectedNodeId.set('a');

  // Assert: panelVariables 'faturalar' + enjekte 'fatura' içerir.
  expect(cmp.panelVariables().map((v) => v.name).sort()).toEqual(['fatura', 'faturalar']);
});

it('does not inject item variables for a node outside any loop', () => {
  const fixture = TestBed.createComponent(DesignerComponent);
  const cmp = fixture.componentInstance;
  cmp.variables.set([]);
  cmp.currentGraph.set({
    schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
    nodes: [{ id: 'x', type: 'activity' }], connections: [],
  });
  cmp.selectedNodeId.set('x');
  expect(cmp.panelVariables()).toEqual([]);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest designer.component`
Expected: FAIL — `cmp.panelVariables is not a function`.

- [ ] **Step 3: Write minimal implementation**

`designer.component.ts` başına import ekle:

```typescript
import { injectedLoopVariables } from './loop-item-schema';
```

`variables` signal tanımından sonra computed ekle (sınıf gövdesi içinde, `readonly variables = signal...` yakınında):

```typescript
/**
 * Properties paneline geçen değişkenler: temel workflow değişkenleri + seçili node'u
 * saran ForEach döngülerinin türetilmiş `item` değişkenleri. Enjekte edilenler kalıcı
 * workflow'a yazılmaz; yalnız autocomplete/alan gösterimi içindir.
 */
readonly panelVariables = computed<WorkflowVariable[]>(() => {
  const base = this.variables();
  const graph = this.currentGraph() ?? this.workflow();
  const nodeId = this.selectedNodeId();
  if (!graph) {
    return base;
  }
  return [...base, ...injectedLoopVariables(nodeId, graph, base)];
});
```

- [ ] **Step 4: Update the template binding**

`designer.component.html` — properties paneline geçen değişkeni değiştir (yalnız properties-panel; variables-panel `variables()` ile kalır):

```html
<app-properties-panel
  ...
  [variables]="panelVariables()"
  ...
></app-properties-panel>
```

(Satır 125'teki `[variables]="variables()"` yalnız `app-properties-panel` bloğunda `panelVariables()` olur.)

- [ ] **Step 5: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx jest designer.component`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/designer.component.ts src/RPA.Studio/src/app/studio/designer/designer.component.html src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts
git commit -m "feat(studio): ForEach item degiskenini properties paneline enjekte et

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Alan-yolu autocomplete (`${item.alan}`) — expression-input + generic-property

**Files:**
- Modify: `src/app/studio/designer/properties/expression-input.component.ts:96-119`
- Modify: `src/app/studio/designer/properties/generic-property.component.ts:163-169`
- Test: `src/app/studio/designer/properties/expression-input.component.spec.ts`
- Test: `src/app/studio/designer/properties/generic-property.component.spec.ts`

**Interfaces:**
- Consumes: `variableFieldPaths` (Task 2).
- Produces: (davranış) — object/list şemalı bir değişken için önerilere `ad.alan` yolları eklenir.

- [ ] **Step 1: Write the failing test (expression-input)**

`expression-input.component.spec.ts` içine ekle:

```typescript
it('suggests schema field paths when the partial matches a field', () => {
  const fixture = TestBed.createComponent(ExpressionInputComponent);
  const cmp = fixture.componentInstance;
  cmp.variables = [{
    name: 'fatura', type: 'object',
    schema: { type: 'object', properties: { tutar: { type: 'number' } } },
  }];
  cmp.updateSuggestions('fatura.tu');
  expect(cmp.suggestions.some((s) => s.insert === '{{fatura.tutar}}')).toBe(true);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest expression-input.component`
Expected: FAIL — no suggestion with insert `{{fatura.tutar}}`.

- [ ] **Step 3: Implement field-path suggestions in expression-input**

`expression-input.component.ts` başına import:

```typescript
import { variableFieldPaths } from '../variable-schema.util';
```

`updateSuggestions` içinde `vars` hesaplamasından hemen sonra, `fns`'ten önce alan yollarını ekle:

```typescript
const fieldPaths: AutocompleteItem[] = (this.variables ?? [])
  .flatMap((v) => variableFieldPaths(v))
  .filter((f) => f.path.toLowerCase().startsWith(q.toLowerCase()))
  .map((f) => ({
    kind: 'variable' as const,
    label: f.path,
    detail: f.type,
    insert: `{{${f.path}}}`,
    caretOffsetFromEnd: 0,
  }));
this.suggestions = [...vars, ...fieldPaths, ...fns];
```

(Mevcut `this.suggestions = [...vars, ...fns];` satırını yukarıdakiyle değiştir.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx jest expression-input.component`
Expected: PASS.

- [ ] **Step 5: Write the failing test (generic-property)**

`generic-property.component.spec.ts` içine ekle:

```typescript
it('expressionSuggestions includes schema field paths', () => {
  const fixture = TestBed.createComponent(GenericPropertyComponent);
  const cmp = fixture.componentInstance;
  cmp.variables = [{
    name: 'fatura', type: 'object',
    schema: { type: 'object', properties: { tutar: { type: 'number' } } },
  }];
  cmp.properties = { message: '{{fatura.tu' };
  const port = { name: 'message', type: 'string' };
  const labels = cmp.expressionSuggestions(port).map((s) => s.name);
  expect(labels).toContain('fatura.tutar');
});
```

- [ ] **Step 6: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest generic-property.component`
Expected: FAIL — `fatura.tutar` yok (yalnız değişken adları öneriliyor).

- [ ] **Step 7: Implement field-path suggestions in generic-property**

`generic-property.component.ts` başına import:

```typescript
import { variableFieldPaths } from '../variable-schema.util';
```

`expressionSuggestions` metodunu, ad + alan yollarını tek bir `WorkflowVariable[]` gibi döndürecek şekilde genişlet (mevcut dönüş tipi `WorkflowVariable[]` korunur; alan yolları `{ name: path, type }` biçiminde sahte WorkflowVariable olarak eklenir — şablon yalnız `.name`'i kullanır):

```typescript
expressionSuggestions(port: ActivityPort): WorkflowVariable[] {
  const partial = this.openTokenPartial(port);
  if (partial === null) return [];
  const q = partial.trim().toLowerCase();
  const names: WorkflowVariable[] = (this.variables ?? []).filter((v) =>
    v.name.toLowerCase().includes(q),
  );
  const fields: WorkflowVariable[] = (this.variables ?? [])
    .flatMap((v) => variableFieldPaths(v))
    .filter((f) => f.path.toLowerCase().includes(q))
    .map((f) => ({ name: f.path, type: f.type }));
  return [...names, ...fields].slice(0, 8);
}
```

Not: `applySuggestion(port, variableName)` zaten `{{${variableName}}}` yazdığından `variableName` bir alan yolu (`fatura.tutar`) olduğunda da doğru çalışır — değişiklik gerekmez.

- [ ] **Step 8: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx jest generic-property.component`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.ts src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.spec.ts src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts
git commit -m "feat(studio): object/list degiskenler icin alan-yolu autocomplete (item.alan)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: ForEach paneli — itemVariable ad doğrulama + fallback alan editörü + kalıcılık

**Files:**
- Modify: `src/app/studio/designer/properties/generic-property.component.ts`
- Modify: `src/app/studio/designer/properties/generic-property.component.html`
- Modify: `src/app/studio/designer/canvas/canvas.component.ts:1235-1238,1277-1281`
- Test: `src/app/studio/designer/properties/generic-property.component.spec.ts`
- Test: `src/app/studio/designer/canvas/canvas.component.spec.ts`

**Interfaces:**
- Consumes: `parseRootVariableName` (Task 1), `LoopItemField` (Task 1).
- Produces:
  - `GenericPropertyComponent.isForEach: boolean`
  - `GenericPropertyComponent.itemVariableError: string`
  - `GenericPropertyComponent.showManualFields: boolean`
  - `GenericPropertyComponent.itemFields: LoopItemField[]` + `addItemField()/updateItemField()/removeItemField()`
  - Canvas: `itemFields` forEach node'unda serialize/extract edilir.

- [ ] **Step 1: Write the failing test (validation + fallback visibility)**

`generic-property.component.spec.ts` içine ekle:

```typescript
it('flags an invalid ForEach itemVariable name', () => {
  const fixture = TestBed.createComponent(GenericPropertyComponent);
  const cmp = fixture.componentInstance;
  (cmp as unknown as { _activityType: string })._activityType = 'Logic.ForEach';
  cmp.properties = { items: '${faturalar}', itemVariable: '1bad' };
  expect(cmp.isForEach).toBe(true);
  cmp.validateItemVariable();
  expect(cmp.itemVariableError).not.toBe('');
});

it('shows manual field editor when items has no resolvable schema variable', () => {
  const fixture = TestBed.createComponent(GenericPropertyComponent);
  const cmp = fixture.componentInstance;
  (cmp as unknown as { _activityType: string })._activityType = 'Logic.ForEach';
  cmp.variables = [];
  cmp.properties = { items: '${hamListe}', itemVariable: 'satir' };
  expect(cmp.showManualFields).toBe(true);
});

it('hides manual field editor when items resolves to a list<object> variable', () => {
  const fixture = TestBed.createComponent(GenericPropertyComponent);
  const cmp = fixture.componentInstance;
  (cmp as unknown as { _activityType: string })._activityType = 'Logic.ForEach';
  cmp.variables = [{
    name: 'faturalar', type: 'list<object>',
    schema: { type: 'array', items: { type: 'object', properties: { tutar: { type: 'number' } } } },
  }];
  cmp.properties = { items: '${faturalar}', itemVariable: 'fatura' };
  expect(cmp.showManualFields).toBe(false);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest generic-property.component`
Expected: FAIL — `cmp.isForEach` undefined.

- [ ] **Step 3: Implement ForEach logic in generic-property**

`generic-property.component.ts` başına import:

```typescript
import { parseRootVariableName, JsonSchemaLike, LoopItemField } from '../loop-item-schema';
```

Sınıf gövdesine ekle:

```typescript
itemVariableError = '';

get isForEach(): boolean {
  return this.activityType === 'Logic.ForEach';
}

/** itemVariable adı geçerli mi? (boş → varsayılan 'item', geçerli sayılır). */
validateItemVariable(): void {
  const name = String(this.properties['itemVariable'] ?? '').trim();
  this.itemVariableError =
    name === '' || /^[A-Za-z_][A-Za-z0-9_]*$/.test(name)
      ? ''
      : 'Gecersiz degisken adi. Harf/alt cizgi ile baslamali.';
}

/** items ifadesi şemalı bir list<object> değişkenine çözülüyor mu? */
private itemsResolvesToSchema(): boolean {
  const root = parseRootVariableName(String(this.properties['items'] ?? ''));
  if (!root) {
    return false;
  }
  const schema = (this.variables.find((v) => v.name === root)?.schema) as JsonSchemaLike | undefined;
  return !!schema && schema.type === 'array' && !!schema.items?.properties;
}

/** Şemaya çözülemediğinde elle alan editörü gösterilir. */
get showManualFields(): boolean {
  return this.isForEach && !this.itemsResolvesToSchema();
}

get itemFields(): LoopItemField[] {
  return (this.properties['itemFields'] as LoopItemField[] | undefined) ?? [];
}

private commitItemFields(fields: LoopItemField[]): void {
  const next = { ...this.properties, itemFields: fields };
  this.properties = next;
  this.propertiesChange.emit(next);
}

addItemField(): void {
  this.commitItemFields([...this.itemFields, { name: '', type: 'string' }]);
}

updateItemField(index: number, patch: Partial<LoopItemField>): void {
  this.commitItemFields(this.itemFields.map((f, i) => (i === index ? { ...f, ...patch } : f)));
}

removeItemField(index: number): void {
  this.commitItemFields(this.itemFields.filter((_, i) => i !== index));
}
```

- [ ] **Step 4: Add the ForEach editor UI to the template**

`generic-property.component.html` — jenerik `inputs` döngüsünden sonra ForEach bloğu ekle (mevcut şablon stiliyle uyumlu; `*ngIf` kullanır):

```html
<div class="foreach-item-editor" *ngIf="isForEach">
  <label class="field-label">{{ 'foreach.itemVariable' | translate }}</label>
  <input
    type="text"
    [value]="properties['itemVariable'] ?? ''"
    (input)="onValueChange({ name: 'itemVariable', type: 'string' }, $any($event.target).value); validateItemVariable()"
    data-testid="foreach-item-variable"
  />
  <p class="field-error" *ngIf="itemVariableError" data-testid="foreach-item-variable-error">
    {{ itemVariableError }}
  </p>

  <div *ngIf="showManualFields" class="manual-fields" data-testid="foreach-manual-fields">
    <p class="hint">{{ 'foreach.manualFieldsHint' | translate }}</p>
    <div class="manual-field-row" *ngFor="let f of itemFields; let i = index">
      <input
        type="text"
        [value]="f.name"
        (input)="updateItemField(i, { name: $any($event.target).value })"
        placeholder="{{ 'foreach.fieldName' | translate }}"
      />
      <select [value]="f.type" (change)="updateItemField(i, { type: $any($event.target).value })">
        <option value="string">string</option>
        <option value="int">int</option>
        <option value="number">number</option>
        <option value="bool">bool</option>
        <option value="datetime">datetime</option>
        <option value="object">object</option>
      </select>
      <button type="button" (click)="removeItemField(i)">✕</button>
    </div>
    <button type="button" (click)="addItemField()" data-testid="foreach-add-field">
      + {{ 'foreach.addField' | translate }}
    </button>
  </div>

  <p *ngIf="!showManualFields" class="hint" data-testid="foreach-auto-hint">
    {{ 'foreach.autoSchemaHint' | translate }}
  </p>
</div>
```

i18n anahtarlarını hem `tr` hem `en` sözlüklerine ekle (mevcut i18n dosyalarını bul: `grep -rl "picker.captureMode" src/app`):
`foreach.itemVariable` = "Eleman değişkeni" / "Item variable",
`foreach.manualFieldsHint` = "Kaynak listenin şeması yok; alanları elle tanımlayın." / "Source list has no schema; define fields manually.",
`foreach.fieldName` = "Alan adı" / "Field name",
`foreach.addField` = "Alan ekle" / "Add field",
`foreach.autoSchemaHint` = "Alanlar kaynak listeden otomatik türetildi." / "Fields auto-derived from the source list."

- [ ] **Step 5: Persist itemFields through canvas serialize/extract**

`canvas.component.ts` — `applyNodePropertiesToWorkflowNode` `case 'forEach'` bloğuna ekle:

```typescript
case 'forEach':
  writable['items'] = props['items'] as string | undefined;
  writable['itemVariable'] = props['itemVariable'] as string | undefined;
  if (Array.isArray(props['itemFields'])) {
    writable['itemFields'] = props['itemFields'];
  }
  break;
```

`extractNodeProperties` `case 'forEach'` bloğuna ekle:

```typescript
case 'forEach':
  return {
    ...(typeof node['items'] === 'string' ? { items: node['items'] } : {}),
    ...(typeof node['itemVariable'] === 'string' ? { itemVariable: node['itemVariable'] } : {}),
    ...(Array.isArray(node['itemFields']) ? { itemFields: node['itemFields'] } : {}),
  };
```

- [ ] **Step 6: Write the failing canvas round-trip test**

`canvas.component.spec.ts` içine ekle (mevcut load→serialize round-trip deseniyle):

```typescript
it('round-trips forEach itemFields through load and serialize', async () => {
  const fixture = TestBed.createComponent(CanvasComponent);
  const canvas = fixture.componentInstance;
  await canvas.ready; // mevcut testlerin editör-hazır beklemesiyle aynı; yoksa fixture.whenStable()
  await canvas.loadWorkflow({
    schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
    nodes: [{ id: 'fe', type: 'forEach', items: '${x}', itemVariable: 'satir',
      itemFields: [{ name: 'id', type: 'string' }] }],
    connections: [],
  });
  const out = canvas.serialize();
  const fe = out.nodes.find((n) => n.type === 'forEach');
  expect(fe?.['itemFields']).toEqual([{ name: 'id', type: 'string' }]);
});
```

(Not: `await canvas.ready` mevcut canvas testlerinin kullandığı hazır-olma mekanizmasına göre uyarlanır; başka canvas testlerinde `loadWorkflow` çağrılan yardımcı varsa onu kullan.)

- [ ] **Step 7: Run tests**

Run: `cd src/RPA.Studio && npx jest generic-property.component canvas.component`
Expected: PASS (yeni testler + mevcutlar).

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.ts src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.spec.ts
# + değiştirilen i18n dosyaları
git commit -m "feat(studio): ForEach panelinde itemVariable dogrulama + fallback alan editoru

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 6: Döngü gövde/çıkış görsel netliği (hafif)

**Files:**
- Modify: `src/app/studio/designer/canvas/node.component.ts:28-44`
- Modify: `src/app/studio/designer/canvas/node.component.html`
- Modify: `src/app/studio/designer/canvas/node.component.scss`
- Modify: `src/app/studio/designer/canvas/canvas.component.ts` (`getOutputPorts`, `getInputPorts`, `bodyHighlightNodeIds` girişi, `toView`)
- Modify: `src/app/studio/designer/designer.component.ts` (`loopBodyHighlightIds` computed)
- Modify: `src/app/studio/designer/designer.component.html` (canvas bağlaması)
- Test: `src/app/studio/designer/canvas/node.component.spec.ts` (varsa) veya `canvas.component.spec.ts`

**Interfaces:**
- Consumes: `enclosingForEachNodes` (Task 1).
- Produces:
  - `CanvasNodeView.bodyHighlight?: boolean`
  - `CanvasComponent.bodyHighlightNodeIds` (@Input, `breakpointNodeIds` deseniyle)
  - `DesignerComponent.loopBodyHighlightIds: Signal<string[]>`

- [ ] **Step 1: Clarify port labels (no test; static strings)**

`canvas.component.ts` `getOutputPorts` içindeki loop dalında etiketleri netleştir:

```typescript
case 'while':
case 'for':
case 'forEach':
  return [
    { port: 'body', label: 'Gövde', tone: 'positive' },
    { port: 'exit', label: 'Çıkış (döngü sonrası)', tone: 'negative' },
  ];
```

`FlowNode` giriş portu etiketleri `toView` içinde üretiliyor (`port === 'loop-back' ? 'Repeat' : 'In'`). Onu netleştir:

```typescript
label: port === 'loop-back' ? 'Gövde sonu / tekrar' : 'In',
```

- [ ] **Step 2: Write the failing test for bodyHighlight view flag**

`node.component.spec.ts` (yoksa `canvas.component.spec.ts`) içine ekle:

```typescript
it('applies the body-highlight class when node.bodyHighlight is true', () => {
  const fixture = TestBed.createComponent(NodeComponent);
  fixture.componentRef.setInput('node', {
    id: 'a', label: 'A', nodeType: 'activity', bodyHighlight: true,
  });
  fixture.detectChanges();
  const root = fixture.nativeElement.querySelector('.canvas-node');
  expect(root.classList).toContain('canvas-node--body-highlight');
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx jest node.component`
Expected: FAIL — sınıf yok.

- [ ] **Step 4: Add the view flag + class**

`node.component.ts` — `CanvasNodeView` arayüzüne ekle:

```typescript
  /** Seçili bir ForEach'in gövdesindeki bir node (hafif vurgu). */
  bodyHighlight?: boolean;
```

`node.component.html` — kök `.canvas-node` elementinin `[class...]` bağlamalarına ekle:

```html
[class.canvas-node--body-highlight]="node.bodyHighlight"
```

`node.component.scss` — stil ekle (mevcut değişken/renk paletiyle uyumlu):

```scss
.canvas-node--body-highlight {
  outline: 2px dashed var(--accent-positive, #3fb950);
  outline-offset: 2px;
}
```

- [ ] **Step 5: Wire bodyHighlightNodeIds into the canvas**

`canvas.component.ts` — `breakpointNodeIds` girişinin hemen altına aynı desende ekle:

```typescript
/** Seçili ForEach'in gövde node id'leri (hafif görsel vurgu). */
@Input()
set bodyHighlightNodeIds(ids: string[]) {
  this._bodyHighlightNodeIds = new Set(ids ?? []);
  this.refreshViews();
}
get bodyHighlightNodeIds(): string[] {
  return [...this._bodyHighlightNodeIds];
}
private _bodyHighlightNodeIds = new Set<string>();
```

`toView` dönüşüne ekle:

```typescript
  bodyHighlight: this._bodyHighlightNodeIds.has(node.id),
```

- [ ] **Step 6: Compute the highlight ids in the designer**

`designer.component.ts` — import ekle:

```typescript
import { enclosingForEachNodes } from './loop-item-schema';
```

Computed ekle:

```typescript
/**
 * Seçili node bir ForEach ise (veya bir ForEach'in gövdesindeyse) vurgulanacak
 * gövde node id'leri. Seçili ForEach'in kendi gövdesini önceler; gövde node'u
 * seçiliyse onu saran (en yakın) döngünün gövdesini vurgular.
 */
readonly loopBodyHighlightIds = computed<string[]>(() => {
  const graph = this.currentGraph() ?? this.workflow();
  const nodeId = this.selectedNodeId();
  if (!graph || !nodeId) {
    return [];
  }
  const selected = graph.nodes.find((n) => n.id === nodeId);
  const loopId =
    selected?.type === 'forEach'
      ? nodeId
      : enclosingForEachNodes(nodeId, graph).at(-1)?.id;
  if (!loopId) {
    return [];
  }
  // bodyNodeIds mantığını yeniden kullan: loop'un gövdesini enclosingForEachNodes
  // üzerinden değil, doğrudan üyelik testiyle çıkar.
  return graph.nodes
    .filter((n) => enclosingForEachNodes(n.id, graph).some((fe) => fe.id === loopId))
    .map((n) => n.id);
});
```

- [ ] **Step 7: Bind in the template**

`designer.component.html` — canvas elementine ekle (mevcut `[breakpointNodeIds]` bağlamasının yanına):

```html
[bodyHighlightNodeIds]="loopBodyHighlightIds()"
```

- [ ] **Step 8: Unconnected-exit hint on loop nodes**

`node.component.ts` — `CanvasNodeView` arayüzüne ekle:

```typescript
  /** Bir döngü node'unun exit portu hiçbir yere bağlı değil (görsel hatırlatma). */
  exitUnconnected?: boolean;
```

`canvas.component.ts` `toView` içine ekle (yalnız loop node'larında anlamlı; exit bağlantısı yoksa true):

```typescript
  exitUnconnected:
    ['forEach', 'for', 'while'].includes(node.nodeType) &&
    !this.editor.getConnections().some(
      (c) => c.source === node.id &&
        (c as unknown as { sourceOutput?: string }).sourceOutput === 'exit',
    ),
```

`node.component.html` — döngü node'unun gövdesine küçük bir uyarı rozeti ekle:

```html
<span
  class="canvas-node__exit-hint"
  *ngIf="node.exitUnconnected"
  data-testid="canvas-node-exit-hint"
  [title]="'foreach.exitUnconnected' | translate"
>⚠</span>
```

i18n: `foreach.exitUnconnected` = "Döngü sonrası akış bağlı değil (exit portu boş)." / "No flow after the loop (exit port is empty)." — `tr` + `en` sözlüklerine ekle.

`node.component.scss` — rozet stili:

```scss
.canvas-node__exit-hint {
  margin-left: 4px;
  color: var(--accent-warning, #d29922);
  cursor: help;
}
```

Test (`node.component.spec.ts`):

```typescript
it('shows the exit hint when a loop node exit is unconnected', () => {
  const fixture = TestBed.createComponent(NodeComponent);
  fixture.componentRef.setInput('node', {
    id: 'fe', label: 'ForEach', nodeType: 'forEach', exitUnconnected: true,
  });
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="canvas-node-exit-hint"]')).toBeTruthy();
});
```

- [ ] **Step 9: Run tests**

Run: `cd src/RPA.Studio && npx jest node.component canvas.component designer.component`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/canvas/node.component.ts src/RPA.Studio/src/app/studio/designer/canvas/node.component.html src/RPA.Studio/src/app/studio/designer/canvas/node.component.scss src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.ts src/RPA.Studio/src/app/studio/designer/designer.component.ts src/RPA.Studio/src/app/studio/designer/designer.component.html
git commit -m "feat(studio): dongu govde vurgusu + net body/exit port etiketleri

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 7: Tam Studio test paketi + build doğrulaması

**Files:** (yok — doğrulama)

- [ ] **Step 1: Run the full Studio unit test suite**

Run: `cd src/RPA.Studio && npm test -- --watch=false`
Expected: Tüm testler yeşil (yeni + mevcut). Kırılan mevcut test varsa düzelt, yeşile al.

- [ ] **Step 2: Production build**

Run: `cd src/RPA.Studio && npm run build`
Expected: 0 hata.

- [ ] **Step 3: Manuel doğrulama (verify skill ile)**

Designer'ı aç, bir `EInvoice.ReadProfileBatch` (veya `list<object>` çıktı veren) node ekle → çıktı değişkeni (`faturalar`) kataloğa düşer. Bir `Logic.ForEach` ekle, `items = {{faturalar}}`, `itemVariable = fatura`. Gövdeye bir aktivite bağla, gövde sonunu ForEach'e `loop-back` ile döndür. Gövde node'unu seç → bir alan ifadesinde `{{fatura.` yazınca `fatura.tutar` önerisi çıkmalı. ForEach'i seçince gövde node'ları vurgulanmalı. `items`'ı şemasız bir değişkene çevir → elle alan editörü belirmeli.

- [ ] **Step 4: Commit (gerekirse düzeltmeler)**

```bash
git add -A
git commit -m "test(studio): ForEach item sema ozelligi tam paket + build dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
