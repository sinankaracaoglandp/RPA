# Yapısal Konteyner Editörü — Alt-proje C2 (Sürükle-Bırak) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yapısal görünümde CDK DragDrop ile paletten lane'lere yeni öğe bırakma + mevcut öğeleri lane içinde/arası sürükleyerek sıralama/taşıma; mutasyon `tree-ops` → `treeToWorkflow` ile taslak kaydına akar (otomatik-tel).

**Architecture:** `tree-ops`'a `findSeqPath`/`reorderInSeq`/`moveAcross` (index-ayarlamalı) eklenir. Her lane + kök `cdkDropList` (tek `cdkDropListGroup`, iç içe otomatik bağlı), öğeler + palet çipleri `cdkDrag`. Host `onDrop`, `cdkDropListData` (dizi referansı) + `findSeqPath` ile adresi çıkarır, tree-op uygular, `treeToWorkflow` yayar. Runtime/kontrat değişmez.

**Tech Stack:** Angular standalone (signals, OnPush), `@angular/cdk/drag-drop`, saf TS, Vitest.

## Global Constraints

- Runtime/`WorkflowSchema.json`/`BaseRunner`/A modülleri değişmez. C1 boru hattı (`commit` → `graphChanged`) korunur; C1 düğmeleri kalır.
- Adresleme **referansla** (`cdkDropListData` = dizi referansı; path `findSeqPath`'ten). Mutasyonlar immutable.
- CDK bileşenleri `@angular/cdk/drag-drop`'tan: `CdkDrag`, `CdkDropList`, `CdkDropListGroup`, tip `CdkDragDrop`.
- Palet dizisi mutasyona uğramaz — bırakmada `factory()` ile YENİ öğe üretilir (transfer değil).
- **Import derinliği:** `structured/edit/` ve `structured/view/`: `../edit/tree-ops`, `../structured-model`, `../tree-to-workflow`; `../../../../shared/...`, `../../../../core/...`.
- i18n hem `tr.json` hem `en.json`.
- Test: `cd src/RPA.Studio && npx ng test --include="**/<spec>" --watch=false`.

---

## Dosya Yapısı

- **Modify:** `structured/edit/tree-ops.ts` (+spec) — `findSeqPath`, `reorderInSeq`, `moveAcross`.
- **Create:** `structured/view/structured-palette.component.ts|html|scss` (+spec).
- **Modify:** `structured/view/structured-item.component.ts|html` — lane `cdkDropList` + öğe `cdkDrag` + `drop` çıkışı.
- **Modify:** `structured/view/structured-sequence.component.ts|html` — kök `cdkDropList` + `drop` çıkışı.
- **Modify:** `structured/view/structured-view.component.ts|html` — `cdkDropListGroup` + palet + `onDrop`.
- **Modify:** i18n `tr.json`/`en.json` — `structured.palette`.

Ortak: palet çip verisi `{ factory: () => StructuredItem }`; öğe drag verisi = `StructuredItem`.

---

### Task 1: tree-ops — `findSeqPath`, `reorderInSeq`, `moveAcross`

**Files:**
- Modify: `src/app/studio/designer/structured/edit/tree-ops.ts`
- Test: `src/app/studio/designer/structured/edit/tree-ops.spec.ts`

**Interfaces:**
- Produces: `findSeqPath(tree, seq): PathStep[] | null`, `reorderInSeq(tree, seqSteps, fromIndex, toIndex)`, `moveAcross(tree, fromSteps, fromIndex, toSteps, toIndex)`.

- [ ] **Step 1: Write failing tests** (mevcut `tree-ops.spec.ts` sonuna ekle)

```typescript
import { findSeqPath, reorderInSeq, moveAcross } from './tree-ops';

describe('tree-ops — drag-drop helpers', () => {
  it('findSeqPath locates the root and a nested lane by reference', () => {
    const body = [step(n('x'))];
    const tree: StructuredSequence = [container('forEach', {}, { body })];
    expect(findSeqPath(tree, tree)).toEqual([]);
    expect(findSeqPath(tree, body)).toEqual([{ lane: 'body', index: 0 }]);
    expect(findSeqPath(tree, [step(n('nope'))])).toBeNull();
  });

  it('reorderInSeq moves within a sequence (moveItemInArray semantics)', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b')), step(n('c'))];
    const out = reorderInSeq(tree, [], 0, 2);
    expect(out.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['b', 'c', 'a']);
  });

  it('moveAcross moves an item between two lanes', () => {
    const tree: StructuredSequence = [
      container('if', {}, { true: [step(n('t0'))], false: [step(n('f0'))] }),
    ];
    // if.true[0] -> if.false[1]
    const out = moveAcross(tree, [{ lane: 'true', index: 0 }], 0, [{ lane: 'false', index: 0 }], 1);
    const c = out[0] as { lanes: { true: unknown[]; false: { node: WorkflowNode }[] } };
    expect(c.lanes.true).toHaveLength(0);
    expect(c.lanes.false.map((i) => i.node.id)).toEqual(['f0', 't0']);
  });

  it('moveAcross into an ancestor sequence (out of a lane to the root) stays correct', () => {
    const inner = step(n('inner'));
    const tree: StructuredSequence = [
      step(n('a')),
      container('forEach', {}, { body: [inner] }),
    ];
    // forEach.body[0] -> root[2] (sona)
    const out = moveAcross(tree, [{ lane: 'body', index: 1 }], 0, [], 2);
    expect((out[0] as { node: WorkflowNode }).node.id).toBe('a');
    expect((out[1] as { lanes: { body: unknown[] } }).lanes.body).toHaveLength(0);
    expect((out[2] as { node: WorkflowNode }).node.id).toBe('inner');
  });

  it('moveAcross adjusts a target path that passes through the source after the removed index', () => {
    const tree: StructuredSequence = [
      step(n('a')),                                   // root[0] (kaldırılacak)
      container('if', {}, { true: [], false: [] }),   // root[1] -> silme sonrası root[0]
    ];
    // root[0] (a) -> if.true[0]; hedef yolu root[1] üstünden geçer, silme sonrası kaymalı
    const out = moveAcross(tree, [], 0, [{ lane: 'true', index: 1 }], 0);
    expect(out).toHaveLength(1); // a kalktı, yalnız if kaldı
    const c = out[0] as { type: string; lanes: { true: { node: WorkflowNode }[] } };
    expect(c.type).toBe('if');
    expect(c.lanes.true.map((i) => i.node.id)).toEqual(['a']);
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

Run: `cd src/RPA.Studio && npx ng test --include="**/edit/tree-ops.spec.ts" --watch=false`

- [ ] **Step 3: Implement (mevcut `tree-ops.ts` sonuna ekle)**

```typescript
/** Bir dizi REFERANSINI ağaçta arar; adım yolunu döndürür (kök = []); yoksa null. */
export function findSeqPath(tree: StructuredSequence, seq: StructuredSequence): PathStep[] | null {
  if (seq === tree) { return []; }
  const walk = (current: StructuredSequence, steps: PathStep[]): PathStep[] | null => {
    for (let i = 0; i < current.length; i++) {
      const item = current[i];
      if (item.kind === 'container') {
        for (const lane of lanesFor(item.type)) {
          const laneSeq = item.lanes[lane] ?? [];
          const here = [...steps, { lane, index: i }];
          if (laneSeq === seq) { return here; }
          const r = walk(laneSeq, here);
          if (r) { return r; }
        }
      }
    }
    return null;
  };
  return walk(tree, []);
}

/** Aynı dizide taşır (CDK moveItemInArray semantiği; ek index ayarı yok). */
export function reorderInSeq(
  tree: StructuredSequence, seqSteps: PathStep[], fromIndex: number, toIndex: number,
): StructuredSequence {
  return updateSeqAt(tree, seqSteps, (seq) => {
    const next = [...seq];
    const [moved] = next.splice(fromIndex, 1);
    next.splice(toIndex, 0, moved);
    return next;
  });
}

/** Diziyi adımlarla dolaşıp döndürür (yardımcı). */
function seqAt(tree: StructuredSequence, steps: PathStep[]): StructuredSequence {
  let seq = tree;
  for (const s of steps) {
    const item = seq[s.index];
    seq = item.kind === 'container' ? (item.lanes[s.lane] ?? []) : [];
  }
  return seq;
}

/**
 * Öğeyi kaynak diziden (fromSteps, fromIndex) hedef diziye (toSteps, toIndex) taşır.
 * Silme, hedef yolu kaynak dizinin ATASINDAN geçiyorsa ve indeks silinenden sonra ise
 * o adımı bir azaltır (index-tabanlı yol tutarlılığı).
 */
export function moveAcross(
  tree: StructuredSequence,
  fromSteps: PathStep[], fromIndex: number,
  toSteps: PathStep[], toIndex: number,
): StructuredSequence {
  const item = seqAt(tree, fromSteps)[fromIndex];
  if (item === undefined) { return tree; }
  const t1 = removeItem(tree, { steps: fromSteps, index: fromIndex });

  // Hedef yolu, kaynak diziyi (fromSteps) prefix olarak paylaşıyor ve bir sonraki adımı
  // silinen indeksten büyükse: o adımın indeksini 1 azalt.
  const adjusted = toSteps.map((s) => ({ ...s }));
  if (adjusted.length > fromSteps.length
    && fromSteps.every((s, i) => s.lane === adjusted[i].lane && s.index === adjusted[i].index)
    && adjusted[fromSteps.length].index > fromIndex) {
    adjusted[fromSteps.length].index -= 1;
  }
  return insertItem(t1, adjusted, toIndex, item);
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/tree-ops.ts src/RPA.Studio/src/app/studio/designer/structured/edit/tree-ops.spec.ts
git commit -m "feat(studio): yapisal editor — tree-ops findSeqPath/reorderInSeq/moveAcross (DnD)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: Sürükle paleti bileşeni

**Files:**
- Create: `structured/view/structured-palette.component.ts|html|scss` (+spec)

**Interfaces:**
- Consumes: `ActivityCatalogService`, `newContainer`/`newStep` (tree-ops), `CdkDrag`, `CdkDropList`.
- Produces: `StructuredPaletteComponent` — sürükle çipleri; her çip `cdkDragData = { factory }`.

- [ ] **Step 1: Write failing test**

```typescript
// structured-palette.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredPaletteComponent } from './structured-palette.component';
import { ContainerItem } from '../structured-model';

describe('StructuredPaletteComponent', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StructuredPaletteComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('exposes control-type chips whose factory builds a container', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([]));
    const chip = f.componentInstance.controlChips.find((c) => c.type === 'if')!;
    expect((chip.factory() as ContainerItem).type).toBe('if');
  });

  it('builds activity chips from the catalog whose factory builds a step', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    const chip = f.componentInstance.activityChips[0];
    expect((chip.factory() as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement**

```typescript
// structured-palette.component.ts
import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../../shared/services/activity-catalog.service';
import { ContainerType, StructuredItem } from '../structured-model';
import { newContainer, newStep } from '../edit/tree-ops';

interface Chip { label: string; factory: () => StructuredItem; }
interface ControlChip extends Chip { type: ContainerType; }

@Component({
  selector: 'app-structured-palette',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, CdkDrag, CdkDropList],
  templateUrl: './structured-palette.component.html',
  styleUrls: ['./structured-palette.component.scss'],
})
export class StructuredPaletteComponent implements OnInit {
  private readonly catalog = inject(ActivityCatalogService);
  private readonly controlTypes: ContainerType[] = ['if', 'forEach', 'for', 'while', 'tryCatch'];

  readonly controlChips: ControlChip[] = this.controlTypes.map((type) => ({
    type, label: 'structured.type.' + type, factory: () => newContainer(type),
  }));
  activityChips: Chip[] = [];

  ngOnInit(): void {
    this.catalog.getActivities().subscribe({
      next: (list) => (this.activityChips = list.map((a) => ({
        label: a.displayName || a.activityId, factory: () => newStep(a.activityId),
      }))),
      error: () => (this.activityChips = []),
    });
  }
}
```

```html
<!-- structured-palette.component.html -->
<div class="palette">
  <span class="palette__title">{{ 'structured.palette' | translate }}</span>
  <div class="palette__chips" cdkDropList [cdkDropListSortingDisabled]="true" [cdkDropListData]="'__palette__'">
    @for (c of controlChips; track c.type) {
      <div class="palette__chip" cdkDrag [cdkDragData]="{ factory: c.factory }">{{ c.label | translate }}</div>
    }
    @for (c of activityChips; track c.label) {
      <div class="palette__chip" cdkDrag [cdkDragData]="{ factory: c.factory }">{{ c.label }}</div>
    }
  </div>
</div>
```

```scss
/* structured-palette.component.scss */
.palette { display: flex; flex-direction: column; gap: 6px; padding: 8px; border-bottom: 1px solid #e2e8f0; }
.palette__title { font-size: 12px; font-weight: 600; color: #475569; }
.palette__chips { display: flex; flex-wrap: wrap; gap: 6px; }
.palette__chip { padding: 4px 8px; border: 1px solid #cbd5e1; border-radius: 6px; background: #fff; cursor: grab; font-size: 12px; }
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.*
git commit -m "feat(studio): yapisal editor — surukle paleti (kontrol + aktivite cipleri)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Öğe/dizi CDK wiring (lane dropList + öğe drag + drop çıkışı)

**Files:**
- Modify: `structured/view/structured-item.component.ts|html`
- Modify: `structured/view/structured-sequence.component.ts|html`
- Test: `structured/view/structured-item.component.spec.ts`

**Interfaces:**
- Produces: item/sequence `@Output() drop = EventEmitter<CdkDragDrop<StructuredSequence>>()`; lane/kök `cdkDropList`, öğe `cdkDrag` (editable-gated).

- [ ] **Step 1: Write failing smoke test**

`structured-item.component.spec.ts` içine ekle:

```typescript
it('renders lanes as cdkDropList and items as cdkDrag when editable', () => {
  const f = TestBed.createComponent(StructuredItemComponent);
  f.componentRef.setInput('item', container('forEach', {}, { body: [step({ id: 'b', type: 'activity', activity: 'A' })] }));
  f.componentRef.setInput('editable', true);
  f.detectChanges();
  expect(f.nativeElement.querySelector('.cdk-drop-list')).toBeTruthy();
  expect(f.nativeElement.querySelector('.cdk-drag')).toBeTruthy();
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement item component**

`structured-item.component.ts` — CDK import + drop çıkışı:
```typescript
import { CdkDrag, CdkDropList, CdkDragDrop } from '@angular/cdk/drag-drop';
// @Component imports'a: CdkDrag, CdkDropList
// sınıfa:
@Output() readonly drop = new EventEmitter<CdkDragDrop<StructuredSequence>>();
```
`structured-item.component.html` — lane'i `cdkDropList`, öğeleri `cdkDrag` yap (yalnız `editable`);
`editable` değilken mevcut düz render (C1/B) korunur. Örnek lane bloğu:
```html
<section class="structured-container__lane" [attr.data-testid]="'lane-' + lane"
         cdkDropList [cdkDropListData]="laneItems(container, lane)"
         [cdkDropListDisabled]="!editable" (cdkDropListDropped)="drop.emit($event)">
  <span class="structured-container__lane-label">{{ 'structured.lane.' + lane | translate }}</span>
  @for (child of laneItems(container, lane); track $index) {
    <app-structured-item [item]="child" [editable]="editable"
      cdkDrag [cdkDragData]="child" [cdkDragDisabled]="!editable"
      (action)="action.emit($event)" (drop)="drop.emit($event)"></app-structured-item>
  }
  @if (laneItems(container, lane).length === 0) {
    <p class="structured-container__lane-empty" data-testid="lane-empty">{{ 'structured.empty' | translate }}</p>
  }
  @if (editable) { <app-structured-add-menu (pick)="onLaneAdd(container, lane, $event)"></app-structured-add-menu> }
</section>
```
Not: `cdkDrag` doğrudan `<app-structured-item>` host elementine konur (öğe kartı sürüklenebilir olur).

- [ ] **Step 4: Implement sequence component (kök dropList)**

`structured-sequence.component.ts` — CDK import + drop çıkışı (item'la aynı desen). `structured-sequence.component.html`:
```html
<div class="structured-sequence" cdkDropList [cdkDropListData]="items"
     [cdkDropListDisabled]="!editable" (cdkDropListDropped)="drop.emit($event)">
  @for (item of items; track $index) {
    <app-structured-item [item]="item" [editable]="editable"
      cdkDrag [cdkDragData]="item" [cdkDragDisabled]="!editable"
      (action)="action.emit($event)" (drop)="drop.emit($event)"></app-structured-item>
  }
  @if (items.length === 0) {
    <p class="structured-sequence__empty" data-testid="sequence-empty">{{ 'structured.empty' | translate }}</p>
  }
</div>
```

- [ ] **Step 5: Run — expect PASS** (mevcut item/sequence testleri + smoke)

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-item.component.* src/RPA.Studio/src/app/studio/designer/structured/view/structured-sequence.component.*
git commit -m "feat(studio): yapisal editor — lane cdkDropList + oge cdkDrag + drop koprusu

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Host `onDrop` + dropListGroup + palet entegrasyonu

**Files:**
- Modify: `structured/view/structured-view.component.ts|html`
- Modify: i18n `tr.json`/`en.json`
- Test: `structured/view/structured-view.component.spec.ts`

**Interfaces:**
- Consumes: `findSeqPath`, `reorderInSeq`, `moveAcross`, `insertItem` (tree-ops); `CdkDragDrop`.
- Produces: `StructuredViewComponent.onDrop(event)`.

- [ ] **Step 1: Write failing tests**

`structured-view.component.spec.ts` içine ekle:

```typescript
function dropEvent(prevData: unknown, contData: unknown, prevIdx: number, curIdx: number, itemData: unknown): never {
  return { previousContainer: { data: prevData }, container: { data: contData },
    previousIndex: prevIdx, currentIndex: curIdx, item: { data: itemData } } as never;
}

it('palette drop inserts a new item and emits workflow', () => {
  const wf = treeToWorkflow([step(n('a'))], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  const cmp = f.componentInstance;
  let emitted: { nodes: unknown[] } | undefined;
  cmp.graphChanged.subscribe((g) => (emitted = g as unknown as { nodes: unknown[] }));
  const root = cmp.tree();
  cmp.onDrop(dropEvent('__palette__', root, 0, 1, { factory: () => newStep('Web.Click') }));
  expect(cmp.tree()).toHaveLength(2);
  expect(emitted!.nodes.length).toBe(2);
});

it('reorders within the root sequence on same-list drop', () => {
  const wf = treeToWorkflow([step(n('a')), step(n('b'))], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  const cmp = f.componentInstance;
  const root = cmp.tree();
  cmp.onDrop(dropEvent(root, root, 0, 1, root[0]));
  expect((cmp.tree()[0] as { node: WorkflowNode }).node.id).toBe('b');
});
```
(`newStep` importu spec başına eklenir.)

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement host**

`structured-view.component.ts`:
```typescript
import { CdkDragDrop, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { StructuredPaletteComponent } from './structured-palette.component';
import { findSeqPath, reorderInSeq, moveAcross } from '../edit/tree-ops';
// @Component imports'a: CdkDropListGroup, StructuredPaletteComponent
// sınıfa:
onDrop(event: CdkDragDrop<StructuredSequence>): void {
  const t = this.tree();
  const toSeq = event.container.data;
  const toSteps = findSeqPath(t, toSeq);
  if (!toSteps) { return; }
  const data = event.item.data as unknown as { factory?: () => StructuredItem };
  let next: StructuredSequence;
  if (data && typeof data.factory === 'function') {
    next = insertItem(t, toSteps, event.currentIndex, data.factory());
  } else if (event.previousContainer === event.container) {
    next = reorderInSeq(t, toSteps, event.previousIndex, event.currentIndex);
  } else {
    const fromSeq = event.previousContainer.data;
    const fromSteps = findSeqPath(t, fromSeq);
    if (!fromSteps) { return; }
    next = moveAcross(t, fromSteps, event.previousIndex, toSteps, event.currentIndex);
  }
  this.commit(next);
}
```
`structured-view.component.html` — `tree` dalını `cdkDropListGroup` ile sar + palet + `(drop)`:
```html
    @case ('tree') {
      <div class="structured-view__toolbar"> … zoom (mevcut) … </div>
      @if (editable) { <app-structured-palette></app-structured-palette> }
      <div #scroll class="structured-view__scroll" … (mevcut pan/wheel) …>
        <div class="structured-view__canvas" data-testid="structured-view-tree" [style.transform]="'scale(' + zoom() + ')'" cdkDropListGroup>
          <app-structured-sequence [items]="tree()" [editable]="editable"
            (action)="onAction($event)" (drop)="onDrop($event)"></app-structured-sequence>
          @if (editable) { <app-structured-add-menu data-testid="root-add" (pick)="addToRoot($event)"></app-structured-add-menu> }
        </div>
      </div>
    }
```

- [ ] **Step 4: i18n**

`tr.json` `structured` bloğuna `"palette": "Palet"`; `en.json` `"palette": "Palette"`.

- [ ] **Step 5: Run — expect PASS**

Run: `cd src/RPA.Studio && npx ng test --include="**/view/structured-view.component.spec.ts" --watch=false`

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.* src/RPA.Studio/public/assets/i18n/tr.json src/RPA.Studio/public/assets/i18n/en.json
git commit -m "feat(studio): yapisal editor — host onDrop + dropListGroup + palet (surukle-birak tam)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: Tam test + build doğrulaması

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

Yapısal görünüme geç, paletten bir "Her Biri İçin"i canvas'a sürükle-bırak, gövdesine bir aktivite sürükle, öğeleri sürükleyerek yeniden sırala ve lane'ler arası taşı — her bırakmada designer "dirty" olur. Fallback workflow'da sürükleme kapalı, C1 düğmeleri yok.

- [ ] **Step 5: Commit (gerekirse)**

```bash
git add -A
git commit -m "test(studio): yapisal editor C2 tam paket dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
