# Yapısal Konteyner Editörü — Alt-proje D1 (Serbest-Graf Göçü) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keyfi serbest-graf `WorkflowVersion`'ı ya yapısal ağaca indirge (dizi/if/döngü) ya da kesin nedenle reddet; B host bunu `reduceWorkflow` ile kullanıp fallback'te sebebi gösterir.

**Architecture:** Yeni saf modül `structured-reducer.ts` — tek-giriş SESE bölgeleri özyinelemeli indirgeyici; `reduceWorkflow(workflow): ReduceResult`. `ReducerError` ile kesin tanı. B host `convert()`'i buna geçer. A'nın `workflowToTree`'si (round-trip testleri için) kalır. Runtime/kontrat değişmez.

**Tech Stack:** saf TS, Angular host, Vitest.

## Global Constraints

- Runtime/`WorkflowSchema.json`/`BaseRunner`/serialize/A modülleri değişmez.
- **Ya-hep-ya-hiç:** `reduceWorkflow` ya `{ok:true, tree}` ya `{ok:false, reason}` döndürür — kısmi ağaç yok.
- tryCatch içeren graf → `{ok:false, reason:'tryCatch …(D2)'}` (kapsam dışı).
- **Import derinliği:** `structured/edit/`: `../structured-model`, `../../../../shared/models/workflow.model`.
- i18n hem `tr.json` hem `en.json`.
- Test: `cd src/RPA.Studio && npx ng test --include="**/<spec>" --watch=false`.

---

## Dosya Yapısı

- **Create:** `structured/edit/structured-reducer.ts` (+spec).
- **Modify:** `structured/view/structured-view.component.ts|html` — `convert()` → `reduceWorkflow`; `fallbackReason` signal + mesaj.
- **Modify:** i18n `tr.json`/`en.json` — `structured.fallbackReason`.

Ortak tip (Task 1):
```typescript
export type ReduceResult = { ok: true; tree: StructuredSequence } | { ok: false; reason: string };
```

---

### Task 1: Reducer çekirdeği — giriş + dizi + döngü + temel if

**Files:**
- Create: `structured/edit/structured-reducer.ts`
- Test: `structured/edit/structured-reducer.spec.ts`

**Interfaces:**
- Consumes: `WorkflowVersion/WorkflowNode`, `StructuredSequence`, `container`, `step`, `ContainerType`, `lanesFor`.
- Produces: `reduceWorkflow(workflow): ReduceResult`, iç `ReducerError`, `reduceRegion`, `convergence` (temel).

- [ ] **Step 1: Write failing tests**

```typescript
// structured-reducer.spec.ts
import { reduceWorkflow } from './structured-reducer';
import { treeToWorkflow } from '../tree-to-workflow';
import { step, container, StructuredSequence } from '../structured-model';
import { WorkflowNode, WorkflowVersion } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });
const seqIds = () => { let i = 0; return () => `c${++i}`; };
const ok = (r: { ok: boolean }) => { if (!r.ok) { throw new Error('beklenen ok:true'); } return r as { ok: true; tree: StructuredSequence }; };

describe('reduceWorkflow — reducible', () => {
  it('reduces a linear sequence', () => {
    const wf = treeToWorkflow([step(n('a')), step(n('b'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect(r.tree.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['a', 'b']);
  });

  it('reduces a forEach loop', () => {
    const wf = treeToWorkflow([container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] }), step(n('after'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect(r.tree).toHaveLength(2);
    expect((r.tree[0] as { type: string }).type).toBe('forEach');
  });

  it('reduces a simple if with converging branches', () => {
    const wf = treeToWorkflow([container('if', { condition: '{{c}} == 1' }, { true: [step(n('t'))], false: [step(n('f'))] }), step(n('after'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    expect((r.tree[0] as { type: string }).type).toBe('if');
    expect((r.tree[1] as { node: WorkflowNode }).node.id).toBe('after');
  });
});

describe('reduceWorkflow — rejected', () => {
  it('rejects multiple entry nodes', () => {
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [n('a'), n('b'), n('c')],
      connections: [{ from: 'a', to: 'c', fromPort: 'out', toPort: 'in' }, { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' }],
    };
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toContain('giriş');
  });

  it('rejects a tryCatch graph with a clear reason', () => {
    const wf = treeToWorkflow([container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })], { idGen: seqIds() });
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toContain('tryCatch');
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

Run: `cd src/RPA.Studio && npx ng test --include="**/edit/structured-reducer.spec.ts" --watch=false`

- [ ] **Step 3: Implement**

```typescript
// structured-reducer.ts
import { WorkflowConnection, WorkflowNode, WorkflowVersion } from '../../../../shared/models/workflow.model';
import { ContainerType, StructuredSequence, container, step } from '../structured-model';

export type ReduceResult = { ok: true; tree: StructuredSequence } | { ok: false; reason: string };

class ReducerError extends Error {}

const LOOP_TYPES = new Set<ContainerType>(['forEach', 'for', 'while']);
const CONTAINER_TYPES = new Set(['forEach', 'for', 'while', 'if', 'tryCatch']);
const STRUCT_KEYS = new Set(['id', 'type', 'position', 'tryNodeId', 'catchNodeId', 'finallyNodeId']);

export function reduceWorkflow(workflow: WorkflowVersion): ReduceResult {
  const byId = new Map(workflow.nodes.map((x) => [x.id, x]));
  const conns = workflow.connections;

  const nonLoop = (c: WorkflowConnection) => c.toPort !== 'loop-back';
  const outEdges = (id: string) => conns.filter((c) => c.from === id && nonLoop(c));
  const outTarget = (id: string, port: string) =>
    conns.find((c) => c.from === id && (c.fromPort ?? 'out') === port && nonLoop(c))?.to ?? null;
  const inCount = (id: string) => conns.filter((c) => c.to === id && nonLoop(c)).length;

  const propsOf = (node: WorkflowNode): Record<string, unknown> => {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(node)) {
      if (!STRUCT_KEYS.has(k) && v !== undefined) { out[k] = v; }
    }
    return out;
  };

  // İleri (loop-back hariç) ulaşılabilir küme, opsiyonel durak.
  const forwardReach = (start: string | null, stopAt: string | null): Set<string> => {
    const set = new Set<string>();
    const stack = start ? [start] : [];
    while (stack.length) {
      const id = stack.pop()!;
      if (id === stopAt || set.has(id)) { continue; }
      set.add(id);
      for (const e of outEdges(id)) { stack.push(e.to); }
    }
    return set;
  };

  // Topolojik indeks (loop-back hariç) — yakınsama sıralaması için.
  const topoIndex = new Map<string, number>();
  {
    const temp = new Set<string>(); const perm = new Set<string>(); const order: string[] = [];
    const visit = (id: string) => {
      if (perm.has(id)) { return; }
      if (temp.has(id)) { return; } // döngü (loop-back hariç olmalı; güvenli)
      temp.add(id);
      for (const e of outEdges(id)) { visit(e.to); }
      temp.delete(id); perm.add(id); order.push(id);
    };
    for (const nd of workflow.nodes) { visit(nd.id); }
    order.reverse().forEach((id, i) => topoIndex.set(id, i));
  }

  const convergence = (ifId: string): string | null => {
    const tHead = outTarget(ifId, 'true');
    const fHead = outTarget(ifId, 'false');
    const t = forwardReach(tHead, ifId);
    const f = forwardReach(fHead, ifId);
    let best: string | null = null;
    for (const x of t) {
      if (f.has(x) && (best === null || (topoIndex.get(x) ?? 0) < (topoIndex.get(best) ?? 0))) { best = x; }
    }
    return best;
  };

  const reduceRegion = (entry: string | null, stop: string | null): StructuredSequence => {
    const seq: StructuredSequence = [];
    let cur = entry;
    const seen = new Set<string>();
    while (cur !== null && cur !== stop) {
      if (seen.has(cur)) { throw new ReducerError('Beklenmeyen tekrar (yapısal değil)'); }
      seen.add(cur);
      const node = byId.get(cur);
      if (!node) { throw new ReducerError(`Bilinmeyen node: '${cur}'`); }
      if (node.type === 'tryCatch') { throw new ReducerError('tryCatch yapısal göçü sonraki fazda (D2)'); }
      if (LOOP_TYPES.has(node.type as ContainerType)) {
        const bodyHead = outTarget(cur, 'body');
        const body = bodyHead ? reduceRegion(bodyHead, cur) : [];
        seq.push(container(node.type as ContainerType, propsOf(node), { body }));
        cur = outTarget(cur, 'exit');
      } else if (node.type === 'if') {
        const conv = convergence(cur);
        const tHead = outTarget(cur, 'true');
        const fHead = outTarget(cur, 'false');
        seq.push(container('if', propsOf(node), {
          true: tHead && tHead !== conv ? reduceRegion(tHead, conv) : [],
          false: fHead && fHead !== conv ? reduceRegion(fHead, conv) : [],
        }));
        cur = conv;
      } else if (CONTAINER_TYPES.has(node.type)) {
        throw new ReducerError(`Desteklenmeyen node: '${node.type}'`);
      } else {
        const outs = outEdges(cur);
        if (outs.length > 1) { throw new ReducerError(`'${cur}' birden çok çıkışa sahip (yapısal değil)`); }
        const nxt = outs[0]?.to ?? null;
        if (nxt !== null && nxt !== stop && inCount(nxt) > 1) {
          throw new ReducerError(`'${nxt}' node'u birden fazla yerden ulaşılıyor (yakınsama yok)`);
        }
        seq.push(step(node));
        cur = nxt;
      }
    }
    return seq;
  };

  const hasIncoming = new Set(conns.filter(nonLoop).map((c) => c.to));
  const entries = workflow.nodes.filter((x) => !hasIncoming.has(x.id));
  if (entries.length !== 1) {
    return { ok: false, reason: entries.length === 0 ? 'Giriş node\'u bulunamadı' : 'Birden fazla giriş node\'u' };
  }
  try {
    return { ok: true, tree: reduceRegion(entries[0].id, null) };
  } catch (e) {
    return { ok: false, reason: e instanceof ReducerError ? e.message : 'Yapısal göç başarısız' };
  }
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/structured-reducer.ts src/RPA.Studio/src/app/studio/designer/structured/edit/structured-reducer.spec.ts
git commit -m "feat(studio): yapisal editor — serbest-graf indirgeyici cekirdegi (dizi/dongu/if)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: If sızıntı/tek-giriş doğrulaması + kesin tanı + iç içe/boş-dal

**Files:**
- Modify: `structured/edit/structured-reducer.ts`
- Test: `structured/edit/structured-reducer.spec.ts`

**Interfaces:**
- Produces: `convergence`/`validateBranches` genişlemesi — indirgenemez if desenlerinde kesin `reason`.

- [ ] **Step 1: Write failing tests**

```typescript
describe('reduceWorkflow — if edge cases', () => {
  it('reduces an if with an empty false branch', () => {
    const wf = treeToWorkflow([container('if', {}, { true: [step(n('t'))], false: [] }), step(n('after'))], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    const iff = r.tree[0] as { type: string; lanes: { true: unknown[]; false: unknown[] } };
    expect(iff.type).toBe('if');
    expect(iff.lanes.false).toHaveLength(0);
  });

  it('reduces a nested loop inside an if branch', () => {
    const wf = treeToWorkflow([
      container('if', {}, {
        true: [container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] })],
        false: [step(n('f'))],
      }),
      step(n('after')),
    ], { idGen: seqIds() });
    const r = ok(reduceWorkflow(wf));
    const iff = r.tree[0] as { lanes: { true: { type: string }[] } };
    expect(iff.lanes.true[0].type).toBe('forEach');
  });

  it('rejects a cross-branch leak with a precise reason', () => {
    // if -> true:t ; false:f ; t --out--> f (bir daldan digerine sizinti); f -> after ; if -exit yok
    const wf: WorkflowVersion = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [{ id: 'if1', type: 'if' }, n('t'), n('f'), n('after')],
      connections: [
        { from: 'if1', to: 't', fromPort: 'true', toPort: 'in' },
        { from: 'if1', to: 'f', fromPort: 'false', toPort: 'in' },
        { from: 't', to: 'f', fromPort: 'out', toPort: 'in' },
        { from: 'f', to: 'after', fromPort: 'out', toPort: 'in' },
      ],
    };
    const r = reduceWorkflow(wf);
    expect(r.ok).toBe(false);
    expect((r as { reason: string }).reason).toMatch(/ulaşıl|sızın|yakınsama/);
  });
});
```

- [ ] **Step 2: Run — expect FAIL** (sızıntı testi; temel indirgeyici yanlış ağaç üretir veya hata vermez)

- [ ] **Step 3: Implement — `convergence`'a bölge doğrulaması ekle**

`structured-reducer.ts` — `convergence`'ı bir doğrulama ile genişlet: yakınsama bulunduktan sonra her
dalın **tek-giriş SESE bölge** olduğunu kanıtla; değilse `ReducerError` fırlat.

```typescript
const branchRegion = (head: string | null, conv: string | null): Set<string> =>
  head && head !== conv ? forwardReach(head, conv) : new Set<string>();

const convergence = (ifId: string): string | null => {
  const tHead = outTarget(ifId, 'true');
  const fHead = outTarget(ifId, 'false');
  const t = forwardReach(tHead, ifId);
  const f = forwardReach(fHead, ifId);
  let conv: string | null = null;
  for (const x of t) {
    if (f.has(x) && (conv === null || (topoIndex.get(x) ?? 0) < (topoIndex.get(conv) ?? 0))) { conv = x; }
  }
  // Doğrulama: dallar ayrık, tek-giriş; conv dışına sızıntı yok.
  const tReg = branchRegion(tHead, conv);
  const fReg = branchRegion(fHead, conv);
  for (const x of tReg) {
    if (fReg.has(x)) { throw new ReducerError(`'${x}' node'u iki daldan ulaşılıyor (yakınsama yok)`); }
  }
  for (const [reg, head] of [[tReg, tHead], [fReg, fHead]] as const) {
    for (const x of reg) {
      // bölge-içi node'a gelen tüm (loop-back hariç) kenarlar ya bölge-içinden ya if'ten (head için) olmalı
      for (const c of conns) {
        if (c.to === x && c.toPort !== 'loop-back') {
          const okSource = reg.has(c.from) || (x === head && c.from === ifId);
          if (!okSource) { throw new ReducerError(`'${x}' node'u bölge dışından ulaşılıyor (yakınsama yok)`); }
        }
      }
      // bölge-içi node'un çıkışları ya bölge-içine ya conv'a gitmeli
      for (const e of outEdges(x)) {
        if (!reg.has(e.to) && e.to !== conv) { throw new ReducerError(`'${x}' node'u dal-içinden bölge dışına atlıyor`); }
      }
    }
  }
  return conv;
};
```
(Eski `convergence` gövdesini bununla değiştir; `branchRegion` yardımcısını ekle.)

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/structured-reducer.ts src/RPA.Studio/src/app/studio/designer/structured/edit/structured-reducer.spec.ts
git commit -m "feat(studio): yapisal editor — if bolge dogrulamasi + kesin indirgenemez tani

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Round-trip uyumu (A çıktısı ↔ reduceWorkflow)

**Files:**
- Test: `structured/edit/structured-reducer.spec.ts`

- [ ] **Step 1: Write test**

```typescript
import { workflowToTree } from '../workflow-to-tree';

describe('reduceWorkflow — A round-trip agreement', () => {
  it('agrees with workflowToTree on structured-authored graphs', () => {
    const trees: StructuredSequence[] = [
      [step(n('a')), step(n('b'))],
      [container('while', { condition: '{{c}}' }, { body: [step(n('x'))] }), step(n('y'))],
      [container('if', {}, { true: [step(n('t'))], false: [step(n('f'))] }), step(n('z'))],
    ];
    for (const t of trees) {
      const wf = treeToWorkflow(t, { idGen: seqIds() });
      const viaReduce = ok(reduceWorkflow(wf)).tree;
      const viaTree = workflowToTree(wf);
      // yapı olarak aynı: kind dizisi + konteyner tipleri
      expect(viaReduce.map((i) => i.kind)).toEqual(viaTree.map((i) => i.kind));
    }
  });
});
```

- [ ] **Step 2: Run — expect PASS** (kırmızıysa reducer'ı A deseniyle uyumlu hale getir)

- [ ] **Step 3: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/structured-reducer.spec.ts
git commit -m "test(studio): reduceWorkflow A round-trip uyumu

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: B host `reduceWorkflow`'a geçiş + fallback sebebi

**Files:**
- Modify: `structured/view/structured-view.component.ts|html`
- Modify: i18n `tr.json`/`en.json`
- Test: `structured/view/structured-view.component.spec.ts`

**Interfaces:**
- Consumes: `reduceWorkflow`. Produces: `fallbackReason` signal.

- [ ] **Step 1: Write failing test**

`structured-view.component.spec.ts`:
```typescript
it('shows the precise reason on a non-reducible workflow', () => {
  const wf = {
    schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
    nodes: [n('a'), n('b'), n('c')],
    connections: [
      { from: 'a', to: 'c', fromPort: 'out', toPort: 'in' },
      { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' },
    ],
  };
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf as never);
  f.detectChanges();
  const el = f.nativeElement.querySelector('[data-testid="structured-view-fallback"]') as HTMLElement;
  expect(el).toBeTruthy();
  expect(el.textContent).toContain('giriş');
});
```
(Mevcut fallback testleri hâlâ geçmeli.)

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement — `convert()` reduceWorkflow'a geçir**

`structured-view.component.ts`:
```typescript
import { reduceWorkflow } from '../edit/structured-reducer';
// sınıfa:
readonly fallbackReason = signal<string>('');
// convert gövdesini değiştir:
private convert(workflow: WorkflowVersion | null): ViewState {
  if (!workflow || workflow.nodes.length === 0) { return { kind: 'empty' }; }
  const r = reduceWorkflow(workflow);
  if (r.ok) { return { kind: 'tree', tree: r.tree }; }
  this.fallbackReason.set(r.reason);
  return { kind: 'fallback' };
}
```
Kullanılmayan importları (`workflowToTree`, `checkStructuralInvariants`, `treeToWorkflow` guard için)
temizle — `treeToWorkflow` hâlâ `commit`/undo'da kullanılıyor, kalır; `workflowToTree`/
`checkStructuralInvariants` artık `convert`'te kullanılmıyorsa kaldır.

`structured-view.component.html` — fallback mesajını sebeple göster:
```html
@case ('fallback') {
  <p class="structured-view__msg" data-testid="structured-view-fallback">
    {{ 'structured.fallback' | translate }}
    @if (fallbackReason()) { <br /><small>{{ 'structured.fallbackReason' | translate }}: {{ fallbackReason() }}</small> }
  </p>
}
```

- [ ] **Step 4: i18n**

`tr.json` `structured`: `"fallbackReason": "Neden"`; `en.json`: `"fallbackReason": "Reason"`.

- [ ] **Step 5: Run — expect PASS** (yeni + mevcut host testleri)

Run: `cd src/RPA.Studio && npx ng test --include="**/view/structured-view.component.spec.ts" --watch=false`

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.* src/RPA.Studio/public/assets/i18n/tr.json src/RPA.Studio/public/assets/i18n/en.json
git commit -m "feat(studio): yapisal gorunum — reduceWorkflow gocu + kesin fallback sebebi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: Tam test + build doğrulaması

- [ ] **Step 1: Structured + edit specs**

Run: `cd src/RPA.Studio && npx ng test --include="**/structured/**/*.spec.ts" --watch=false`
Expected: yeşil.

- [ ] **Step 2: Full suite**

Run: `cd src/RPA.Studio && npx ng test --watch=false`
Expected: tümü yeşil. (Not: B/C host testlerinden `convert` guard'a bağlı olanlar reduceWorkflow ile
aynı sonucu vermeli — çok-giriş grafı fallback, forEach tree. Kırılan olursa reducer/testi uzlaştır.)

- [ ] **Step 3: Build**

Run: `cd src/RPA.Studio && npx ng build 2>&1 | tail -20`
Expected: yeni koddan TS hatası YOK (`einvoice-mapping-editor.component.scss` bütçe hatası önceden var olan, ilgisiz).

- [ ] **Step 4: Manuel doğrulama (verify skill)**

Serbest-graf tasarımcıda yapısal bir workflow (dizi/if/döngü) kur, kaydet; yapısal görünüme geç →
iç içe kutular render + düzenlenebilir. İki-girişli/çapraz-dallı bir graf kur → fallback + kesin sebep.

- [ ] **Step 5: Commit (gerekirse)**

```bash
git add -A
git commit -m "test(studio): yapisal editor D1 tam paket dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
