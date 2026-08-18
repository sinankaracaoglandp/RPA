# Yapısal Konteyner Editörü — Alt-proje C3 (Seçim + Özellik Paneli + Undo/Redo) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yapısal modda node'a tıklayıp mevcut özellik panelinde parametre düzenleme + geçmiş yığınıyla geri-al/yinele.

**Architecture:** `tree-ops`'a `updateItemAt`/`setItemProps` eklenir. `StructuredViewComponent` bir `selected` signal + `nodeSelect` çıkışı tutar; designer mevcut sağ paneli besler ve `propertiesChange`'i (ViewChild ile) `updateSelectedProps`'a yönlendirir → ağaç güncellenir → `commit` → `graphChanged`. `commit` bir geçmiş yığını tutar (props edit'leri koalese edilir); undo/redo düğme + klavye. Runtime/kontrat değişmez.

**Tech Stack:** Angular standalone (signals, OnPush), saf TS, Vitest.

## Global Constraints

- Runtime/`WorkflowSchema.json`/`BaseRunner`/A modülleri değişmez. C1 düğmeleri, C2 sürükle-bırak, B pan/zoom/palet korunur.
- Panel yalnız workflow değişkenleri: `panelVariables()` bağlaması değişmez (yapısal modda graf-seçimi yok → enjeksiyon boş).
- Mutasyonlar immutable; adresleme `findPath`/`findSeqPath` (referans).
- **Import derinliği:** `structured/edit/`, `structured/view/`: `../edit/tree-ops`, `../structured-model`; `../../../../shared/...`, `../../../../core/...`.
- i18n hem `tr.json` hem `en.json`.
- Test: `cd src/RPA.Studio && npx ng test --include="**/<spec>" --watch=false`.

---

## Dosya Yapısı

- **Modify:** `structured/edit/tree-ops.ts` (+spec) — `updateItemAt`, `setItemProps`.
- **Create:** `structured/edit/control-activity-map.ts` — `CONTROL_ACTIVITY_OF`.
- **Modify:** `structured/view/structured-item.component.ts|html|scss` — seçim tıklaması + seçili stil + `select` çıkışı + `selectedRef` girişi.
- **Modify:** `structured/view/structured-sequence.component.ts|html` — `select` yeniden-yayını + `selectedRef` köprüsü.
- **Modify:** `structured/view/structured-view.component.ts|html` — `selected` + `nodeSelect` + `updateSelectedProps` + undo/redo.
- **Modify:** `designer.component.ts|html` — yapısal `nodeSelect` bağlama + `onPropertiesChange` yapısal dallanma + `viewChild(StructuredViewComponent)`.
- **Modify:** i18n `tr.json`/`en.json` — `structured.undo`/`structured.redo`.

---

### Task 1: tree-ops — `updateItemAt`, `setItemProps`

**Files:**
- Modify: `structured/edit/tree-ops.ts`
- Test: `structured/edit/tree-ops.spec.ts`

**Interfaces:**
- Produces: `updateItemAt(tree, path, fn): StructuredSequence`, `setItemProps(tree, path, props): StructuredSequence`.

- [ ] **Step 1: Write failing tests** (`tree-ops.spec.ts` sonuna)

```typescript
import { updateItemAt, setItemProps } from './tree-ops';

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
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement (tree-ops.ts sonuna)**

```typescript
/** path'teki öğeyi fn ile değiştirir (immutable). */
export function updateItemAt(
  tree: StructuredSequence, path: Path, fn: (item: StructuredItem) => StructuredItem,
): StructuredSequence {
  return updateSeqAt(tree, path.steps, (seq) => seq.map((it, i) => (i === path.index ? fn(it) : it)));
}

/** Öğenin parametrelerini değiştirir: adım → node.properties; konteyner → props. */
export function setItemProps(
  tree: StructuredSequence, path: Path, props: Record<string, unknown>,
): StructuredSequence {
  return updateItemAt(tree, path, (item) =>
    item.kind === 'step'
      ? { ...item, node: { ...item.node, properties: props } }
      : { ...item, props });
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/tree-ops.ts src/RPA.Studio/src/app/studio/designer/structured/edit/tree-ops.spec.ts
git commit -m "feat(studio): yapisal editor — tree-ops updateItemAt/setItemProps

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: `CONTROL_ACTIVITY_OF` + öğe seçimi (item/sequence)

**Files:**
- Create: `structured/edit/control-activity-map.ts`
- Modify: `structured/view/structured-item.component.ts|html|scss`
- Modify: `structured/view/structured-sequence.component.ts|html`
- Test: `structured/view/structured-item.component.spec.ts`

**Interfaces:**
- Produces: `CONTROL_ACTIVITY_OF: Record<ContainerType, string>`; item/sequence `@Input() selectedRef`, `@Output() select`.

- [ ] **Step 1: Write failing test**

`structured-item.component.spec.ts`:
```typescript
it('emits select with the item reference on card click', () => {
  const f = TestBed.createComponent(StructuredItemComponent);
  const item = step({ id: 's', type: 'activity', activity: 'A' });
  f.componentRef.setInput('item', item);
  f.componentRef.setInput('editable', true);
  f.detectChanges();
  let selected: unknown;
  f.componentInstance.select.subscribe((i) => (selected = i));
  (f.nativeElement.querySelector('[data-testid="structured-step"]') as HTMLElement).click();
  expect(selected).toBe(item);
});

it('marks the selected item', () => {
  const f = TestBed.createComponent(StructuredItemComponent);
  const item = step({ id: 's', type: 'activity', activity: 'A' });
  f.componentRef.setInput('item', item);
  f.componentRef.setInput('selectedRef', item);
  f.detectChanges();
  expect(f.nativeElement.querySelector('.structured-item--selected')).toBeTruthy();
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement control-activity-map**

```typescript
// structured/edit/control-activity-map.ts
import { ContainerType } from '../structured-model';

export const CONTROL_ACTIVITY_OF: Record<ContainerType, string> = {
  if: 'Logic.If', forEach: 'Logic.ForEach', for: 'Logic.For',
  while: 'Logic.While', tryCatch: 'Logic.TryCatch',
};
```

- [ ] **Step 4: Implement item component selection**

`structured-item.component.ts` — ekle:
```typescript
@Input() selectedRef: StructuredItem | null = null;
@Output() readonly select = new EventEmitter<StructuredItem>();

onSelect(event: Event): void {
  event.stopPropagation();
  this.select.emit(this.item);
}
get isSelected(): boolean { return this.item === this.selectedRef; }
```
`structured-item.component.html` — adım kartına ve konteyner başlığına `(click)="onSelect($event)"`
ve seçili sınıf `[class.structured-item--selected]="isSelected"`; düğme/drag/add-menu tıklamaları
zaten kendi handler'larında `stopPropagation` (item-up/down/delete `emitAction` içine
`$event.stopPropagation()` eklenir). Çocuk `app-structured-item`'e `[selectedRef]="selectedRef"`
ve `(select)="select.emit($event)"` köprüsü. `structured-item.component.scss`:
```scss
.structured-item--selected { box-shadow: 0 0 0 2px #2563eb; }
```
(Seçili sınıf, adım kartı ve konteyner kutusunun kök elementine konur.)

- [ ] **Step 5: Implement sequence bridge**

`structured-sequence.component.ts` — `@Input() selectedRef` + `@Output() select`; template'te
`<app-structured-item [selectedRef]="selectedRef" (select)="select.emit($event)" …>`.

- [ ] **Step 6: Run — expect PASS**

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/control-activity-map.ts src/RPA.Studio/src/app/studio/designer/structured/view/structured-item.component.* src/RPA.Studio/src/app/studio/designer/structured/view/structured-sequence.component.*
git commit -m "feat(studio): yapisal editor — oge secimi (select cikisi + secili stil)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Host seçim + `updateSelectedProps`

**Files:**
- Modify: `structured/view/structured-view.component.ts|html`
- Test: `structured/view/structured-view.component.spec.ts`

**Interfaces:**
- Consumes: `findPath`, `setItemProps`, `seqAt`-benzeri (dahili), `CONTROL_ACTIVITY_OF`.
- Produces: `selected` signal, `@Output() nodeSelect`, `onSelect(item)`, `clearSelection()`, `updateSelectedProps(props)`.

- [ ] **Step 1: Write failing test**

`structured-view.component.spec.ts`:
```typescript
it('emits nodeSelect with activityType/properties when a step is selected', () => {
  const wf = treeToWorkflow([step(n('a'))], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  const cmp = f.componentInstance;
  let sel: { activityType?: string; properties: Record<string, unknown> } | null = null;
  cmp.nodeSelect.subscribe((s) => (sel = s));
  cmp.onSelect(cmp.tree()[0]);
  expect(sel!.activityType).toBe('X');
});

it('updateSelectedProps updates the selected item and emits workflow', () => {
  const wf = treeToWorkflow([container('forEach', { items: '${a}' }, { body: [] }), step(n('after'))], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  const cmp = f.componentInstance;
  cmp.onSelect(cmp.tree()[0]);
  let emitted = false;
  cmp.graphChanged.subscribe(() => (emitted = true));
  cmp.updateSelectedProps({ items: '${b}', itemVariable: 'x' });
  expect((cmp.tree()[0] as { props: unknown }).props).toEqual({ items: '${b}', itemVariable: 'x' });
  expect(emitted).toBe(true);
});
```
(`step`/`container` importları spec'te mevcut.)

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement**

`structured-view.component.ts`:
```typescript
import { findPath, setItemProps } from '../edit/tree-ops';
import { CONTROL_ACTIVITY_OF } from '../edit/control-activity-map';
import { ContainerType } from '../structured-model';

export interface StructuredSelection { activityType?: string; properties: Record<string, unknown>; }

// sınıfa:
readonly selected = signal<StructuredItem | null>(null);
@Output() readonly nodeSelect = new EventEmitter<StructuredSelection | null>();

onSelect(item: StructuredItem): void {
  this.selected.set(item);
  this.nodeSelect.emit(this.selectionOf(item));
}
clearSelection(): void {
  this.selected.set(null);
  this.nodeSelect.emit(null);
}
private selectionOf(item: StructuredItem): StructuredSelection {
  if (item.kind === 'step') {
    return { activityType: item.node.activity, properties: (item.node.properties as Record<string, unknown>) ?? {} };
  }
  return { activityType: CONTROL_ACTIVITY_OF[item.type as ContainerType], properties: { ...item.props } };
}

updateSelectedProps(props: Record<string, unknown>): void {
  const sel = this.selected();
  if (!sel) { return; }
  const p = findPath(this.tree(), sel);
  if (!p) { return; }
  const next = setItemProps(this.tree(), p, props);
  this.commit(next, { props: true });
  // seçili referansı yeni ağaçtaki öğeye taşı (sonraki edit'ler için taze)
  const fresh = this.itemAt(next, p);
  if (fresh) { this.selected.set(fresh); }
}

private itemAt(tree: StructuredSequence, p: { steps: { lane: import('../structured-model').LaneName; index: number }[]; index: number }): StructuredItem | null {
  let seq = tree;
  for (const s of p.steps) {
    const it = seq[s.index];
    if (it.kind !== 'container') { return null; }
    seq = it.lanes[s.lane] ?? [];
  }
  return seq[p.index] ?? null;
}
```
**`commit` imzasını bu task'ta opts-alan hale getir** (opts Task 4'e kadar yok sayılır), böylece
`updateSelectedProps`'un `commit(next, { props: true })` çağrısı derlenir:
```typescript
private commit(next: StructuredSequence, _opts: { props?: boolean } = {}): void {
  this.tree.set(next);
  this.graphChanged.emit(treeToWorkflow(next));
}
```
Mevcut C1 `commit(next)` çağrıları (`onAction`/`onDrop`/`addToRoot`) değişmeden geçer (opts
varsayılan `{}`). Task 4 bu gövdeyi geçmiş + koalesleme mantığıyla değiştirir.

`structured-view.component.html` — `tree` dalında:
- `app-structured-sequence`'e `[selectedRef]="selected()"` + `(select)="onSelect($event)"`.
- Canvas boş alanına tıklama seçimi kaldırır: `<div class="structured-view__canvas" … (click)="clearSelection()">` (öğe tıklamaları `stopPropagation` ile buraya ulaşmaz).

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.*
git commit -m "feat(studio): yapisal editor — host secim + updateSelectedProps

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Undo/Redo (geçmiş + koalesleme + düğme + klavye)

**Files:**
- Modify: `structured/view/structured-view.component.ts|html`
- Modify: i18n `tr.json`/`en.json`
- Test: `structured/view/structured-view.component.spec.ts`

**Interfaces:**
- Produces: `undo()`, `redo()`, `canUndo`/`canRedo`; `commit(next, opts?)` geçmiş + koalesleme.

- [ ] **Step 1: Write failing test**

```typescript
it('undo restores the previous tree and clears selection', () => {
  const wf = treeToWorkflow([step(n('a'))], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  const cmp = f.componentInstance;
  cmp.addToRoot(step(n('b')));         // mutasyon
  expect(cmp.tree()).toHaveLength(2);
  expect(cmp.canUndo).toBe(true);
  let cleared = false;
  cmp.nodeSelect.subscribe((s) => { if (s === null) { cleared = true; } });
  cmp.undo();
  expect(cmp.tree()).toHaveLength(1);
  expect(cleared).toBe(true);
  cmp.redo();
  expect(cmp.tree()).toHaveLength(2);
});

it('coalesces consecutive prop edits into one undo step', () => {
  const wf = treeToWorkflow([container('forEach', { items: '${a}' }, { body: [] })], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  const cmp = f.componentInstance;
  cmp.onSelect(cmp.tree()[0]);
  cmp.updateSelectedProps({ items: '${b}' });
  cmp.updateSelectedProps({ items: '${bc}' });
  cmp.undo(); // iki prop edit tek adımda geri alınır
  expect((cmp.tree()[0] as { props: { items: string } }).props.items).toBe('${a}');
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement**

`structured-view.component.ts` — `commit`'i geçmişli yap:
```typescript
private past: StructuredSequence[] = [];
private future: StructuredSequence[] = [];
private propsEditing = false;

get canUndo(): boolean { return this.past.length > 0; }
get canRedo(): boolean { return this.future.length > 0; }

private commit(next: StructuredSequence, opts: { props?: boolean } = {}): void {
  if (!(opts.props && this.propsEditing)) {
    this.past.push(this.tree());
    this.future = [];
  }
  this.propsEditing = !!opts.props;
  this.tree.set(next);
  this.graphChanged.emit(treeToWorkflow(next));
}

undo(): void {
  if (!this.canUndo) { return; }
  this.future.push(this.tree());
  this.tree.set(this.past.pop()!);
  this.propsEditing = false;
  this.clearSelection();
  this.graphChanged.emit(treeToWorkflow(this.tree()));
}
redo(): void {
  if (!this.canRedo) { return; }
  this.past.push(this.tree());
  this.tree.set(this.future.pop()!);
  this.propsEditing = false;
  this.clearSelection();
  this.graphChanged.emit(treeToWorkflow(this.tree()));
}

@HostListener('document:keydown', ['$event'])
onKeydown(event: KeyboardEvent): void {
  if (!this.editable) { return; }
  const tag = (event.target as HTMLElement)?.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') { return; } // input undo'yu ele geçirme
  if (!(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== 'z' && event.key.toLowerCase() !== 'y') { return; }
  event.preventDefault();
  const redo = (event.key.toLowerCase() === 'z' && event.shiftKey) || event.key.toLowerCase() === 'y';
  redo ? this.redo() : this.undo();
}
```
`@HostListener` için `import { HostListener } from '@angular/core';`. Not: T3'teki opts-alan `commit`
gövdesini bu geçmişli sürümle değiştir; `onAction`/`onDrop`/`addToRoot` çağrıları değişmez (props
olmayan mutasyon → `propsEditing=false`). **Ek:** `onSelect` ve `clearSelection`'a
`this.propsEditing = false;` ekle (yeni seçimde prop koalesleme zinciri kırılsın — sonraki node'un
ilk edit'i yeni bir undo adımı olsun).

`structured-view.component.html` — toolbar'a düğmeler:
```html
<button type="button" data-testid="structured-undo" [disabled]="!canUndo" (click)="undo()">↶</button>
<button type="button" data-testid="structured-redo" [disabled]="!canRedo" (click)="redo()">↷</button>
```

- [ ] **Step 4: i18n**

`tr.json` `structured`: `"undo": "Geri al", "redo": "Yinele"`; `en.json`: `"undo": "Undo", "redo": "Redo"` (düğme `title`'larında kullanılabilir).

- [ ] **Step 5: Run — expect PASS**

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.* src/RPA.Studio/public/assets/i18n/tr.json src/RPA.Studio/public/assets/i18n/en.json
git commit -m "feat(studio): yapisal editor — undo/redo (gecmis + prop koalesleme + klavye)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: Designer wiring (seçim → panel → props geri akışı)

**Files:**
- Modify: `designer.component.ts|html`
- Test: `designer.component.spec.ts`

**Interfaces:**
- Consumes: `StructuredViewComponent` (ViewChild), `StructuredSelection`.

- [ ] **Step 1: Write failing test**

`designer.component.spec.ts` structured describe'ına:
```typescript
it('feeds the properties panel from a structured selection and routes changes back', () => {
  const fixture = TestBed.createComponent(DesignerComponent);
  const cmp = fixture.componentInstance;
  fixture.detectChanges();
  cmp.toggleStructuredView();
  fixture.detectChanges();
  cmp.onStructuredSelect({ activityType: 'Logic.ForEach', properties: { items: '${a}' } });
  expect(cmp.selectedActivityType()).toBe('Logic.ForEach');
  expect(cmp.selectedProperties()).toEqual({ items: '${a}' });

  // yapısal moddayken propertiesChange structured-view'a yönlenir (canvas'a değil)
  cmp.onPropertiesChange({ items: '${b}' });
  expect(cmp.selectedProperties()).toEqual({ items: '${b}' });
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement designer**

`designer.component.ts`:
```typescript
import { StructuredViewComponent } from './structured/view/structured-view.component';
// sınıfa:
readonly structuredViewRef = viewChild(StructuredViewComponent);

onStructuredSelect(sel: { activityType?: string; properties: Record<string, unknown> } | null): void {
  this.selectedActivityType.set(sel?.activityType);
  this.selectedProperties.set(sel?.properties ?? {});
}
```
`onPropertiesChange` başına yapısal dallanma ekle:
```typescript
onPropertiesChange(properties: Record<string, unknown>): void {
  if (this.structuredView()) {
    this.selectedProperties.set(properties);
    this.structuredViewRef()?.updateSelectedProps(properties);
    return;
  }
  // ... mevcut canvas yolu ...
}
```
`designer.component.html` — structured-view bağlamasına `(nodeSelect)="onStructuredSelect($event)"` ekle:
```html
<app-structured-view
  [workflow]="currentGraph() ?? workflow()"
  (graphChanged)="onGraphChanged($event)"
  (nodeSelect)="onStructuredSelect($event)"
></app-structured-view>
```

- [ ] **Step 4: Run — expect PASS**

Run: `cd src/RPA.Studio && npx ng test --include="**/designer.component.spec.ts" --watch=false`

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/designer.component.ts src/RPA.Studio/src/app/studio/designer/designer.component.html src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts
git commit -m "feat(studio): designer — yapisal secim ozellik panelini besler + props geri akisi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 6: Tam test + build doğrulaması

- [ ] **Step 1: Structured + edit specs**

Run: `cd src/RPA.Studio && npx ng test --include="**/structured/**/*.spec.ts" --watch=false`
Expected: yeşil.

- [ ] **Step 2: Full suite**

Run: `cd src/RPA.Studio && npx ng test --watch=false`
Expected: tümü yeşil.

- [ ] **Step 3: Build**

Run: `cd src/RPA.Studio && npx ng build 2>&1 | tail -20`
Expected: yeni koddan TS hatası YOK (`einvoice-mapping-editor.component.scss` bütçe hatası önceden var olan, ilgisiz).

- [ ] **Step 4: Manuel doğrulama (verify skill)**

Yapısal görünüme geç, bir node'a tıkla → sağ panelde parametreleri düzenle → değişiklik anında uygulanır (dirty). Ctrl+Z ile geri al, Ctrl+Shift+Z ile yinele. Palet/sürükle/[+ekle] mutasyonları da undo'ya girer. Fallback workflow'da seçim/düzenleme yok.

- [ ] **Step 5: Commit (gerekirse)**

```bash
git add -A
git commit -m "test(studio): yapisal editor C3 tam paket dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
