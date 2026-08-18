# Yapısal Konteyner Editörü — Alt-proje A (Model + Köprü) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yapısal ağaç belge modelini ve `ağaç ⇄ WorkflowVersion` iki yönlü köprüsünü saf TypeScript olarak kurmak.

**Architecture:** Angular'dan bağımsız saf modül (`src/app/studio/designer/structured/`). `treeToWorkflow` yapısal ağacı düz nodes+connections'a çevirir (diziler `out/in`; döngüler `body/loop-back/exit`; if `true/false`+yakınsama; tryCatch `tryNodeId/catchNodeId/finallyNodeId` özellikleri). `workflowToTree` yapısal alt-kümeyi geri okur. Runtime/`WorkflowSchema.json`/`BaseRunner` değişmez.

**Tech Stack:** TypeScript, Vitest (Angular `@angular/build:unit-test`, `ng test --include=<spec>`).

## Global Constraints

- Yalnız Studio; saf TypeScript; Angular/Rete bağımlılığı YOK. Runtime, `WorkflowSchema.json`, `BaseRunner` değiştirilmez.
- Üretilen düz graf, mevcut `canvas.serialize()` + `BaseRunner` sözleşmesiyle **birebir uyumlu** olmalı: tryCatch çocukları bağlantı DEĞİL node özelliğidir (`tryNodeId/catchNodeId/finallyNodeId`); döngü `body`+`loop-back`+`exit`; if `true`/`false`; diziler `fromPort:'out'`, `toPort:'in'`.
- `position` A tarafından üretilmez.
- Node id üretimi enjekte edilebilir (`idGen`) — testler deterministik olsun. Varsayılan `crypto.randomUUID`.
- `WorkflowNode`, `WorkflowConnection`, `WorkflowVersion`, `ConnectionPort`, `ConnectionTargetPort` tipleri `../../shared/models/workflow.model`'den gelir; yeni tip icat edilmez.
- Test komutu: `cd src/RPA.Studio && npx ng test --include="**/<spec>" --watch=false`.
- Studio'da workflow şema doğrulayıcı YOK → golden test, düz grafın yapısal değişmezlerini kontrol eden `structural-invariants.ts` ile yapılır (ajv/yeni bağımlılık eklenmez).

---

## Dosya Yapısı

- **Create:** `src/app/studio/designer/structured/structured-model.ts` — tipler + yapıcılar (`step`, `container`, `lanesFor`).
- **Create:** `src/app/studio/designer/structured/tree-to-workflow.ts` — `treeToWorkflow` + iç yardımcılar (`emitSequence`, `emitItem`, `OpenTail`).
- **Create:** `src/app/studio/designer/structured/workflow-to-tree.ts` — `workflowToTree`.
- **Create:** `src/app/studio/designer/structured/structural-invariants.ts` — `checkStructuralInvariants(workflow)` (golden test yardımcısı).
- **Create:** her modül için `*.spec.ts`.

Interface sözleşmeleri (görevler arası, birebir):
- `type OpenTail = { nodeId: string; fromPort: ConnectionPort }`
- `type EmitResult = { head: string | null; tails: OpenTail[] }`
- `treeToWorkflow(tree: StructuredSequence, opts?: TreeToWorkflowOptions): WorkflowVersion`
- `type TreeToWorkflowOptions = { id?: string; name?: string; version?: string; idGen?: () => string }`
- `workflowToTree(workflow: WorkflowVersion): StructuredSequence`
- `checkStructuralInvariants(workflow: WorkflowVersion): string[]` (ihlal mesajları; boş = geçerli)

---

### Task 1: Yapısal model tipleri + yapıcılar

**Files:**
- Create: `src/app/studio/designer/structured/structured-model.ts`
- Test: `src/app/studio/designer/structured/structured-model.spec.ts`

**Interfaces:**
- Consumes: `WorkflowNode` from `../../shared/models/workflow.model`.
- Produces: `StructuredSequence`, `StructuredItem`, `StepItem`, `ContainerItem`, `ContainerType`, `LaneName`, `step()`, `container()`, `lanesFor()`.

- [ ] **Step 1: Write the failing test**

```typescript
// structured-model.spec.ts
import { step, container, lanesFor } from './structured-model';
import { WorkflowNode } from '../../shared/models/workflow.model';

describe('structured-model', () => {
  it('wraps a workflow node as a step item', () => {
    const node: WorkflowNode = { id: 'n1', type: 'activity', activity: 'Web.Click' };
    expect(step(node)).toEqual({ kind: 'step', node });
  });

  it('builds a container item with props and lanes', () => {
    const c = container('forEach', { items: '${x}' }, { body: [] });
    expect(c.kind).toBe('container');
    expect(c.type).toBe('forEach');
    expect(c.props).toEqual({ items: '${x}' });
    expect(c.lanes.body).toEqual([]);
  });

  it('lists valid lanes per container type', () => {
    expect(lanesFor('while')).toEqual(['body']);
    expect(lanesFor('if')).toEqual(['true', 'false']);
    expect(lanesFor('tryCatch')).toEqual(['success', 'failure', 'out']);
  });
});
```

- [ ] **Step 2: Run test — expect FAIL (module not found)**

Run: `cd src/RPA.Studio && npx ng test --include="**/structured-model.spec.ts" --watch=false`

- [ ] **Step 3: Implement**

```typescript
// structured-model.ts
import { WorkflowNode } from '../../shared/models/workflow.model';

export type ContainerType = 'forEach' | 'for' | 'while' | 'if' | 'tryCatch';
export type LaneName = 'body' | 'true' | 'false' | 'success' | 'failure' | 'out';

export interface StepItem {
  kind: 'step';
  node: WorkflowNode;
}

export interface ContainerItem {
  kind: 'container';
  type: ContainerType;
  props: Record<string, unknown>;
  lanes: Partial<Record<LaneName, StructuredSequence>>;
}

export type StructuredItem = StepItem | ContainerItem;
export type StructuredSequence = StructuredItem[];

export function step(node: WorkflowNode): StepItem {
  return { kind: 'step', node };
}

export function container(
  type: ContainerType,
  props: Record<string, unknown>,
  lanes: Partial<Record<LaneName, StructuredSequence>>,
): ContainerItem {
  return { kind: 'container', type, props, lanes };
}

/** Konteyner tipinin geçerli lane adları (mevcut port adlarıyla birebir). */
export function lanesFor(type: ContainerType): LaneName[] {
  switch (type) {
    case 'if':
      return ['true', 'false'];
    case 'tryCatch':
      return ['success', 'failure', 'out'];
    default:
      return ['body'];
  }
}
```

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/structured-model.ts src/RPA.Studio/src/app/studio/designer/structured/structured-model.spec.ts
git commit -m "feat(studio): yapisal konteyner editoru — belge modeli tipleri + yapicilar

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: `treeToWorkflow` — düz diziler (step'ler, konteyner yok)

**Files:**
- Create: `src/app/studio/designer/structured/tree-to-workflow.ts`
- Test: `src/app/studio/designer/structured/tree-to-workflow.spec.ts`

**Interfaces:**
- Consumes: Task 1 tipleri; `WorkflowNode/WorkflowConnection/WorkflowVersion/ConnectionPort`.
- Produces: `treeToWorkflow`, `OpenTail`, `EmitResult`, iç `emitSequence`/`emitItem` (dosya-özel).

- [ ] **Step 1: Write the failing test**

```typescript
// tree-to-workflow.spec.ts
import { treeToWorkflow } from './tree-to-workflow';
import { step } from './structured-model';
import { WorkflowNode } from '../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('treeToWorkflow — linear sequences', () => {
  it('emits an empty workflow for an empty tree', () => {
    const wf = treeToWorkflow([]);
    expect(wf.nodes).toEqual([]);
    expect(wf.connections).toEqual([]);
    expect(wf.schemaVersion).toBe('1.0');
  });

  it('links consecutive steps with out/in connections', () => {
    const wf = treeToWorkflow([step(n('a')), step(n('b')), step(n('c'))]);
    expect(wf.nodes.map((x) => x.id)).toEqual(['a', 'b', 'c']);
    expect(wf.connections).toEqual([
      { from: 'a', to: 'b', fromPort: 'out', toPort: 'in' },
      { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' },
    ]);
  });

  it('applies id/name/version from options', () => {
    const wf = treeToWorkflow([step(n('a'))], { id: 'w1', name: 'Demo', version: '2.0.0' });
    expect(wf).toMatchObject({ id: 'w1', name: 'Demo', version: '2.0.0' });
  });
});
```

- [ ] **Step 2: Run test — expect FAIL**

- [ ] **Step 3: Implement (linear only; container branches throw for now)**

```typescript
// tree-to-workflow.ts
import {
  ConnectionPort, WorkflowConnection, WorkflowNode, WorkflowVersion,
} from '../../shared/models/workflow.model';
import { ContainerItem, StructuredItem, StructuredSequence } from './structured-model';

export interface OpenTail {
  nodeId: string;
  fromPort: ConnectionPort;
}

export interface EmitResult {
  head: string | null;
  tails: OpenTail[];
}

export interface TreeToWorkflowOptions {
  id?: string;
  name?: string;
  version?: string;
  idGen?: () => string;
}

interface Builder {
  nodes: WorkflowNode[];
  connections: WorkflowConnection[];
  idGen: () => string;
}

export function treeToWorkflow(
  tree: StructuredSequence,
  opts: TreeToWorkflowOptions = {},
): WorkflowVersion {
  const builder: Builder = {
    nodes: [],
    connections: [],
    idGen: opts.idGen ?? (() => crypto.randomUUID()),
  };
  emitSequence(tree, builder);
  return {
    schemaVersion: '1.0',
    id: opts.id ?? crypto.randomUUID(),
    name: opts.name ?? 'Untitled',
    version: opts.version ?? '1.0.0',
    nodes: builder.nodes,
    connections: builder.connections,
  };
}

/** Diziyi yayar; ardışık öğeleri bağlar. Head = ilk öğe head'i; tails = son öğe tail'leri. */
function emitSequence(seq: StructuredSequence, b: Builder): EmitResult {
  let head: string | null = null;
  let prevTails: OpenTail[] = [];
  for (const item of seq) {
    const r = emitItem(item, b);
    if (r.head === null) {
      continue;
    }
    if (head === null) {
      head = r.head;
    }
    for (const t of prevTails) {
      b.connections.push({ from: t.nodeId, to: r.head, fromPort: t.fromPort, toPort: 'in' });
    }
    prevTails = r.tails;
  }
  return { head, tails: prevTails };
}

function emitItem(item: StructuredItem, b: Builder): EmitResult {
  if (item.kind === 'step') {
    b.nodes.push(item.node);
    return { head: item.node.id, tails: [{ nodeId: item.node.id, fromPort: 'out' }] };
  }
  return emitContainer(item, b);
}

// Konteynerler Task 3-5'te doldurulur.
function emitContainer(item: ContainerItem, _b: Builder): EmitResult {
  throw new Error(`emitContainer not implemented for '${item.type}'`);
}
```

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.ts src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.spec.ts
git commit -m "feat(studio): treeToWorkflow — dogrusal dizi baglama

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: `treeToWorkflow` — döngüler (forEach/for/while)

**Files:**
- Modify: `src/app/studio/designer/structured/tree-to-workflow.ts` (`emitContainer`)
- Test: `src/app/studio/designer/structured/tree-to-workflow.spec.ts`

**Interfaces:**
- Produces: `emitContainer` döngü dalı — konteyner node `type` + `props`, `body`/`loop-back`/`exit` bağlantıları.

- [ ] **Step 1: Write the failing test**

```typescript
// tree-to-workflow.spec.ts içine ekle
import { container } from './structured-model';

describe('treeToWorkflow — loops', () => {
  it('wires body, loop-back and exit for a forEach', () => {
    const tree = [
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('bodyA'))] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    const fe = wf.nodes.find((x) => x.type === 'forEach')!;
    expect(fe.id).toBe('c1');
    expect(fe['items']).toBe('${xs}');
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'bodyA', fromPort: 'body', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'bodyA', to: 'c1', fromPort: 'out', toPort: 'loop-back' });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'after', fromPort: 'exit', toPort: 'in' });
  });
});

// Dosya başına deterministik id üreteci ekle:
function seqIds(): () => string {
  let i = 0;
  return () => `c${++i}`;
}
```

- [ ] **Step 2: Run test — expect FAIL (`emitContainer not implemented`)**

- [ ] **Step 3: Implement the loop branch in `emitContainer`**

```typescript
function emitContainer(item: ContainerItem, b: Builder): EmitResult {
  const id = b.idGen();
  const node: WorkflowNode = { id, type: item.type, ...item.props };
  b.nodes.push(node);

  if (item.type === 'forEach' || item.type === 'for' || item.type === 'while') {
    const body = emitSequence(item.lanes.body ?? [], b);
    if (body.head !== null) {
      b.connections.push({ from: id, to: body.head, fromPort: 'body', toPort: 'in' });
      for (const t of body.tails) {
        b.connections.push({ from: t.nodeId, to: id, fromPort: t.fromPort, toPort: 'loop-back' });
      }
    }
    return { head: id, tails: [{ nodeId: id, fromPort: 'exit' }] };
  }

  throw new Error(`emitContainer not implemented for '${item.type}'`);
}
```

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.ts src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.spec.ts
git commit -m "feat(studio): treeToWorkflow — dongu body/loop-back/exit baglama

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: `treeToWorkflow` — if (true/false + yakınsama + boş dal)

**Files:**
- Modify: `src/app/studio/designer/structured/tree-to-workflow.ts` (`emitContainer` if dalı)
- Test: `src/app/studio/designer/structured/tree-to-workflow.spec.ts`

**Interfaces:**
- Produces: `emitContainer` if dalı — `true`/`false` bağlantıları; konteyner tail'leri = iki dalın tail'leri (boş dalda if node'un ilgili portu).

- [ ] **Step 1: Write the failing test**

```typescript
describe('treeToWorkflow — if', () => {
  it('wires both branches and converges to the next item', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [step(n('f'))] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 't', fromPort: 'true', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'f', fromPort: 'false', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 't', to: 'after', fromPort: 'out', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'f', to: 'after', fromPort: 'out', toPort: 'in' });
  });

  it('an empty branch flows directly to the successor via its port', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    // true dalı: t -> after ; false dalı boş: if -false-> after
    expect(wf.connections).toContainEqual({ from: 't', to: 'after', fromPort: 'out', toPort: 'in' });
    expect(wf.connections).toContainEqual({ from: 'c1', to: 'after', fromPort: 'false', toPort: 'in' });
  });
});
```

- [ ] **Step 2: Run test — expect FAIL**

- [ ] **Step 3: Implement the if branch in `emitContainer` (before the final throw)**

```typescript
  if (item.type === 'if') {
    const tails: OpenTail[] = [];
    for (const [lane, port] of [['true', 'true'], ['false', 'false']] as const) {
      const r = emitSequence(item.lanes[lane] ?? [], b);
      if (r.head !== null) {
        b.connections.push({ from: id, to: r.head, fromPort: port, toPort: 'in' });
        tails.push(...r.tails);
      } else {
        // Boş dal: if node'un o portu doğrudan ardıla akar.
        tails.push({ nodeId: id, fromPort: port });
      }
    }
    return { head: id, tails };
  }
```

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.ts src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.spec.ts
git commit -m "feat(studio): treeToWorkflow — if true/false + dal yakinsamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: `treeToWorkflow` — tryCatch (özellik-tabanlı çocuklar + finally-tail devamı)

**Files:**
- Modify: `src/app/studio/designer/structured/tree-to-workflow.ts` (`emitContainer` tryCatch dalı)
- Test: `src/app/studio/designer/structured/tree-to-workflow.spec.ts`

**Interfaces:**
- Produces: `emitContainer` tryCatch dalı — `tryNodeId`/`catchNodeId`/`finallyNodeId` node özellikleri; success/failure lane'leri iç zincir (tail açık); konteyner tail'i = finally(out) lane tail'i; finally boşsa örtük `merge` geçişi.

- [ ] **Step 1: Write the failing test**

```typescript
describe('treeToWorkflow — tryCatch', () => {
  it('stores children as node properties and continues from the finally tail', () => {
    const tree = [
      container('tryCatch', { exceptionVariable: 'ex' }, {
        success: [step(n('tryA'))],
        failure: [step(n('catchA'))],
        out: [step(n('finA'))],
      }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    const tc = wf.nodes.find((x) => x.type === 'tryCatch')!;
    expect(tc['tryNodeId']).toBe('tryA');
    expect(tc['catchNodeId']).toBe('catchA');
    expect(tc['finallyNodeId']).toBe('finA');
    // tryCatch cocuklari BAGLANTI olarak yazilmaz:
    expect(wf.connections.some((c) => c.from === tc.id && c.fromPort === 'success')).toBe(false);
    // devam finally tail'inden:
    expect(wf.connections).toContainEqual({ from: 'finA', to: 'after', fromPort: 'out', toPort: 'in' });
  });

  it('inserts an implicit merge passthrough when finally is empty', () => {
    const tree = [
      container('tryCatch', {}, { success: [step(n('tryA'))], failure: [step(n('catchA'))], out: [] }),
      step(n('after')),
    ];
    const wf = treeToWorkflow(tree, { idGen: seqIds() });
    const tc = wf.nodes.find((x) => x.type === 'tryCatch')!;
    const mergeId = tc['finallyNodeId'] as string;
    expect(wf.nodes.find((x) => x.id === mergeId)?.type).toBe('merge');
    expect(wf.connections).toContainEqual({ from: mergeId, to: 'after', fromPort: 'out', toPort: 'in' });
  });
});
```

- [ ] **Step 2: Run test — expect FAIL**

- [ ] **Step 3: Implement the tryCatch branch in `emitContainer`**

```typescript
  if (item.type === 'tryCatch') {
    const writable = node as Record<string, unknown>;
    const success = emitSequence(item.lanes.success ?? [], b);
    const failure = emitSequence(item.lanes.failure ?? [], b);
    let out = emitSequence(item.lanes.out ?? [], b);

    if (success.head !== null) { writable['tryNodeId'] = success.head; }
    if (failure.head !== null) { writable['catchNodeId'] = failure.head; }

    // Finally boşsa: devam noktası için tek-node'luk örtük 'merge' geçişi ekle.
    if (out.head === null) {
      const mergeId = b.idGen();
      b.nodes.push({ id: mergeId, type: 'merge' });
      out = { head: mergeId, tails: [{ nodeId: mergeId, fromPort: 'out' }] };
    }
    writable['finallyNodeId'] = out.head!;

    return { head: id, tails: out.tails };
  }
```

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.ts src/RPA.Studio/src/app/studio/designer/structured/tree-to-workflow.spec.ts
git commit -m "feat(studio): treeToWorkflow — tryCatch ozellik-tabanli cocuklar + finally devami

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 6: Yapısal değişmez kontrolü + golden (iç içe) test

**Files:**
- Create: `src/app/studio/designer/structured/structural-invariants.ts`
- Test: `src/app/studio/designer/structured/structural-invariants.spec.ts`

**Interfaces:**
- Consumes: `WorkflowVersion`, `treeToWorkflow`.
- Produces: `checkStructuralInvariants(workflow): string[]`.

- [ ] **Step 1: Write the failing test**

```typescript
// structural-invariants.spec.ts
import { checkStructuralInvariants } from './structural-invariants';
import { treeToWorkflow } from './tree-to-workflow';
import { step, container } from './structured-model';
import { WorkflowNode } from '../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('checkStructuralInvariants', () => {
  it('accepts a nested loop/if/tryCatch graph produced by treeToWorkflow', () => {
    const tree = [
      container('forEach', { items: '${xs}', itemVariable: 'x' }, {
        body: [
          container('if', { condition: '{{x}} == 1' }, {
            true: [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [] })],
            false: [step(n('elseStep'))],
          }),
        ],
      }),
      step(n('done')),
    ];
    let i = 0;
    const wf = treeToWorkflow(tree, { idGen: () => `c${++i}` });
    expect(checkStructuralInvariants(wf)).toEqual([]);
  });

  it('flags a loop missing its loop-back edge', () => {
    const wf = treeToWorkflow([container('while', {}, { body: [step(n('b'))] })], { idGen: () => 'L' });
    wf.connections = wf.connections.filter((c) => c.toPort !== 'loop-back');
    expect(checkStructuralInvariants(wf)).toContain('while node L: loop-back kenari yok');
  });
});
```

- [ ] **Step 2: Run test — expect FAIL**

- [ ] **Step 3: Implement**

```typescript
// structural-invariants.ts
import { WorkflowVersion } from '../../shared/models/workflow.model';

const LOOP_TYPES = new Set(['forEach', 'for', 'while']);

/**
 * Düz grafın yapısal değişmezlerini kontrol eder (Studio'da şema doğrulayıcı olmadığından
 * treeToWorkflow çıktısının golden doğrulaması için). İhlal mesajları döner; boş = geçerli.
 */
export function checkStructuralInvariants(workflow: WorkflowVersion): string[] {
  const errors: string[] = [];
  const ids = new Set(workflow.nodes.map((x) => x.id));

  // 1) Bağlantı uçları var olan node'lara işaret etmeli.
  for (const c of workflow.connections) {
    if (!ids.has(c.from)) { errors.push(`baglanti kaynagi eksik: ${c.from}`); }
    if (!ids.has(c.to)) { errors.push(`baglanti hedefi eksik: ${c.to}`); }
  }

  for (const node of workflow.nodes) {
    const out = workflow.connections.filter((c) => c.from === node.id);
    if (LOOP_TYPES.has(node.type)) {
      // 2) Döngü: tam bir body + tam bir loop-back + en fazla bir exit.
      if (!out.some((c) => c.fromPort === 'body')) { errors.push(`${node.type} node ${node.id}: body kenari yok`); }
      if (!workflow.connections.some((c) => c.to === node.id && c.toPort === 'loop-back')) {
        errors.push(`${node.type} node ${node.id}: loop-back kenari yok`);
      }
      if (out.filter((c) => c.fromPort === 'exit').length > 1) { errors.push(`${node.type} node ${node.id}: birden fazla exit`); }
    }
    if (node.type === 'if') {
      // 3) If: true ve false portlarının her biri tam bir kenar taşımalı.
      if (out.filter((c) => c.fromPort === 'true').length !== 1) { errors.push(`if node ${node.id}: tam bir true kenari yok`); }
      if (out.filter((c) => c.fromPort === 'false').length !== 1) { errors.push(`if node ${node.id}: tam bir false kenari yok`); }
    }
    if (node.type === 'tryCatch') {
      // 4) TryCatch: cocuklar ozellik olarak; en az tryNodeId ve finallyNodeId tanimli, gecerli id.
      const rec = node as unknown as Record<string, unknown>;
      for (const key of ['tryNodeId', 'finallyNodeId']) {
        const v = rec[key];
        if (typeof v !== 'string' || !ids.has(v)) { errors.push(`tryCatch node ${node.id}: ${key} gecersiz`); }
      }
      // Cocuklar baglanti olarak yazilmamali.
      if (out.some((c) => ['success', 'failure', 'out'].includes(c.fromPort ?? ''))) {
        errors.push(`tryCatch node ${node.id}: cocuklar baglanti olarak yazilmis`);
      }
    }
  }
  return errors;
}
```

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/structural-invariants.ts src/RPA.Studio/src/app/studio/designer/structured/structural-invariants.spec.ts
git commit -m "feat(studio): yapisal degismez kontrolu (golden dogrulama)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 7: `workflowToTree` — ters köprü (yapısal alt-küme) + round-trip

**Files:**
- Create: `src/app/studio/designer/structured/workflow-to-tree.ts`
- Test: `src/app/studio/designer/structured/workflow-to-tree.spec.ts`

**Interfaces:**
- Consumes: Task 1 tipleri; `WorkflowVersion`.
- Produces: `workflowToTree(workflow): StructuredSequence`.

- [ ] **Step 1: Write the failing test**

```typescript
// workflow-to-tree.spec.ts
import { workflowToTree } from './workflow-to-tree';
import { treeToWorkflow } from './tree-to-workflow';
import { step, container, StructuredSequence } from './structured-model';
import { WorkflowNode } from '../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });
let counter = 0;
const ids = () => `c${++counter}`;
beforeEach(() => { counter = 0; });

function roundTrip(tree: StructuredSequence): StructuredSequence {
  return workflowToTree(treeToWorkflow(tree, { idGen: ids }));
}

describe('workflowToTree — round-trip', () => {
  it('round-trips a linear sequence', () => {
    const tree = [step(n('a')), step(n('b'))];
    expect(roundTrip(tree)).toEqual(tree);
  });

  it('round-trips a forEach with a body', () => {
    const tree = [
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('bodyA'))] }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    expect(back[0].kind).toBe('container');
    const c = back[0] as { type: string; lanes: { body: unknown[] } };
    expect(c.type).toBe('forEach');
    expect(c.lanes.body).toHaveLength(1);
  });

  it('round-trips an if with converging branches', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [step(n('f'))] }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    expect((back[0] as { type: string }).type).toBe('if');
    expect((back[1] as { node: WorkflowNode }).node.id).toBe('after');
  });

  it('round-trips a nested loop inside an if branch', () => {
    const tree = [
      container('if', { condition: '{{c}} == 1' }, {
        true: [container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] })],
        false: [step(n('f'))],
      }),
      step(n('after')),
    ];
    const back = roundTrip(tree);
    expect(back).toHaveLength(2);
    const iff = back[0] as { type: string; lanes: { true: unknown[] } };
    expect(iff.type).toBe('if');
    expect((iff.lanes.true[0] as { type: string }).type).toBe('forEach');
  });

  it('throws for tryCatch reverse (Faz-A kapsamı dışı)', () => {
    const wf = treeToWorkflow(
      [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
      { idGen: ids },
    );
    expect(() => workflowToTree(wf)).toThrow(/tryCatch ters/);
  });
});
```

- [ ] **Step 2: Run test — expect FAIL**

- [ ] **Step 3: Implement**

```typescript
// workflow-to-tree.ts
import { WorkflowNode, WorkflowVersion } from '../../shared/models/workflow.model';
import {
  ContainerItem, ContainerType, StructuredSequence, container, step,
} from './structured-model';

const LOOP_TYPES = new Set<ContainerType>(['forEach', 'for', 'while']);
const CONTAINER_TYPES = new Set(['forEach', 'for', 'while', 'if', 'tryCatch']);
// Bir konteyner node'unun WorkflowNode üzerindeki taşınmayan/temizlenecek yapısal anahtarları.
const STRUCT_KEYS = new Set(['id', 'type', 'position', 'tryNodeId', 'catchNodeId', 'finallyNodeId']);

/** Yapısal alt-küme düz grafı yapısal ağaca çevirir (A'nın kendi ürettiği iyi-biçimli graf). */
export function workflowToTree(workflow: WorkflowVersion): StructuredSequence {
  const byId = new Map(workflow.nodes.map((x) => [x.id, x]));
  const conns = workflow.connections;

  // Giriş: hiçbir kenarın (loop-back dahil) hedefi olmayan node.
  const hasIncoming = new Set(conns.map((c) => c.to));
  const entry = workflow.nodes.find((x) => !hasIncoming.has(x.id))?.id ?? null;

  const readSequence = (startId: string | null, stopAt: Set<string>): StructuredSequence => {
    const seq: StructuredSequence = [];
    let current = startId;
    const seen = new Set<string>();
    while (current && !stopAt.has(current) && !seen.has(current)) {
      seen.add(current);
      const node = byId.get(current)!;
      if (CONTAINER_TYPES.has(node.type)) {
        const { item, successor } = readContainer(node);
        seq.push(item);
        current = successor;
      } else {
        seq.push(step(node));
        current = outTarget(current, 'out');
      }
    }
    return seq;
  };

  const outTarget = (from: string, port: string): string | null =>
    conns.find((c) => c.from === from && (c.fromPort ?? 'out') === port)?.to ?? null;

  const propsOf = (node: WorkflowNode): Record<string, unknown> => {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(node)) {
      if (!STRUCT_KEYS.has(k) && v !== undefined) { out[k] = v; }
    }
    return out;
  };

  function readContainer(node: WorkflowNode): { item: ContainerItem; successor: string | null } {
    const type = node.type as ContainerType;
    if (LOOP_TYPES.has(type)) {
      const bodyHead = outTarget(node.id, 'body');
      const body = readSequence(bodyHead, new Set([node.id])); // loop-back node'da durur
      const successor = outTarget(node.id, 'exit');
      return { item: container(type, propsOf(node), { body }), successor };
    }
    if (type === 'if') {
      const trueHead = outTarget(node.id, 'true');
      const falseHead = outTarget(node.id, 'false');
      const successor = convergence(node.id);
      const stop = new Set<string>(successor ? [successor] : []);
      return {
        item: container('if', propsOf(node), {
          true: trueHead && trueHead !== successor ? readSequence(trueHead, stop) : [],
          false: falseHead && falseHead !== successor ? readSequence(falseHead, stop) : [],
        }),
        successor,
      };
    }
    // tryCatch ters köprüsü Faz-A kapsamı dışıdır: finally lane'i ile "sonrası" akışı düz grafta
    // contiguous'tur (finally tail'inin 'out' kenarı doğrudan sonraki node'a gider; ayrı sınır
    // işareti yoktur — bkz. BaseRunner.ExecuteTryCatchAsync finally bloğu + spec §3 tryCatch notu).
    // Sınır ancak B/C fazında runtime golden testleriyle güvenle sabitlenebilir. İleri köprü
    // (treeToWorkflow) tryCatch için tamdır; yalnız ters yön ertelenir.
    throw new Error('workflowToTree: tryCatch ters köprüsü Faz-A kapsamı dışı');
  }

  // Bir node'un yapısal ardılı (dizide sonraki öğenin head'i), konteyner tipine göre.
  function successorOf(id: string): string | null {
    const node = byId.get(id)!;
    if (LOOP_TYPES.has(node.type as ContainerType)) { return outTarget(id, 'exit'); }
    if (node.type === 'if') { return convergence(id); }
    if (node.type === 'tryCatch') { throw new Error('workflowToTree: tryCatch ters köprüsü Faz-A kapsamı dışı'); }
    return outTarget(id, 'out');
  }

  // Başlangıçtan ileri (yapısal ardıl) sıralı zincir. loop-back izlenmez (successorOf 'exit' kullanır).
  function forwardChain(start: string | null): string[] {
    const order: string[] = [];
    let cur = start;
    const seen = new Set<string>();
    while (cur && !seen.has(cur)) { seen.add(cur); order.push(cur); cur = successorOf(cur); }
    return order;
  }

  // If yakınsaması: iki dalın ileri-zincirlerinin ilk ortak node'u = ortak ardıl (successor).
  // Boş dalda outTarget(if, port) doğrudan successor'a işaret eder → zincir orada başlar, hemen kesişir.
  function convergence(ifId: string): string | null {
    const tHead = outTarget(ifId, 'true');
    const fHead = outTarget(ifId, 'false');
    const tChain = tHead ? forwardChain(tHead) : [];
    const fSet = new Set(fHead ? forwardChain(fHead) : []);
    for (const x of tChain) { if (fSet.has(x)) { return x; } }
    return null;
  }

  return readSequence(entry, new Set());
}
```

> **Uygulama notu (Task 7):** Ters köprü A'da **loops + if + doğrusal** ile sınırlıdır; `successorOf`
> tryCatch'e rastlarsa açık bir hata fırlatır (yukarıdaki test bunu doğrular). `forwardChain`/
> `convergence` özyinelemesi iç içe döngü/if'i doğal olarak ele alır (loop-back izlenmez). tryCatch
> ters köprüsü, finally/after sınırı runtime golden testiyle sabitlenene dek (B/C fazı) ertelenmiştir;
> ileri köprü (Task 5) tryCatch için tamdır. Round-trip testlerini kırmızıdan yeşile sürerek doğrula.

- [ ] **Step 4: Run test — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/workflow-to-tree.ts src/RPA.Studio/src/app/studio/designer/structured/workflow-to-tree.spec.ts
git commit -m "feat(studio): workflowToTree — ters kopru (yapisal alt-kume) + round-trip

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 8: Tam Studio test paketi + build doğrulaması

**Files:** (yok — doğrulama)

- [ ] **Step 1: Run the structured module specs together**

Run: `cd src/RPA.Studio && npx ng test --include="**/structured/*.spec.ts" --watch=false`
Expected: tüm structured testleri yeşil.

- [ ] **Step 2: Full Studio unit suite**

Run: `cd src/RPA.Studio && npx ng test --watch=false`
Expected: tümü yeşil (yeni + mevcut). Kırılan varsa düzelt.

- [ ] **Step 3: Type-check/build**

Run: `cd src/RPA.Studio && npx ng build 2>&1 | tail -20`
Expected: Yeni `structured/` kodu kaynaklı hata YOK. (Not: `einvoice-mapping-editor.component.scss` bütçe hatası önceden var olan, bu işle ilgisiz bir sorundur; yeni TypeScript hatası olmamalı.)

- [ ] **Step 4: Commit (gerekirse)**

```bash
git add -A
git commit -m "test(studio): yapisal konteyner alt-proje A tam paket dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
