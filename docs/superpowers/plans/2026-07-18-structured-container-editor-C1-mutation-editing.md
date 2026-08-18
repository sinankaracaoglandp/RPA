# Yapısal Konteyner Editörü — Alt-proje C1 (Mutasyon + Minimal Etkileşim) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yapısal görünümü, değişebilir ağaç doğruluk-kaynağı üzerinde minimal denetimlerle (ekle/sil/sırala) düzenlenebilir yapmak; her mutasyon `treeToWorkflow` ile mevcut taslak kaydına akar.

**Architecture:** Saf `tree-ops` modülü (insert/remove/move/findPath + yapıcılar). `StructuredViewComponent` değişebilir `tree` signal tutar (workflow'dan bir kez tohumlanır), mutasyonları `findPath`+tree-ops ile uygular ve `graphChanged` (WorkflowVersion) yayar; designer'ın mevcut `onGraphChanged` yolu kalıcılığı sağlar. `StructuredItemComponent` [sil]/[↑]/[↓]/[+ ekle] denetimleri + referanslı olay çıkışları; küçük `add-menu` kontrol tipleri + aktivite açılır listesi. Runtime/kontrat değişmez.

**Tech Stack:** Angular standalone (signals, OnPush), saf TS, Vitest (`ng test --include=<spec> --watch=false`).

## Global Constraints

- Runtime/`WorkflowSchema.json`/`BaseRunner`/A modülleri değişmez.
- **Import derinliği:** `structured/edit/` ve `structured/view/` bir seviye derin. `../structured-model`, `../workflow-to-tree`, `../tree-to-workflow` (bir üst = `structured/`); `shared/models/workflow.model` için `../../../../shared/...`; `core/translate.pipe` için `../../../../core/...`; `shared/services/...` için `../../../../shared/services/...`.
- Mutasyonlar **immutable** (yeni ağaç). UI olayları öğe **referansı** taşır; path `findPath` ile çıkar.
- Yalnız `mode==='tree'` iken düzenleme denetimleri görünür; `fallback`/`empty`'de gizli.
- i18n hem `tr.json` hem `en.json` (`structured.*` genişler).
- Test komutu: `cd src/RPA.Studio && npx ng test --include="**/<spec>" --watch=false`.

---

## Dosya Yapısı

- **Create:** `src/app/studio/designer/structured/edit/tree-ops.ts` + `tree-ops.spec.ts`.
- **Create:** `src/app/studio/designer/structured/view/structured-add-menu.component.ts|html|scss` + spec.
- **Modify:** `structured/view/structured-item.component.ts|html` — düzenleme denetimleri + `action` çıkışı + çocuk olay köprüsü.
- **Modify:** `structured/view/structured-sequence.component.ts|html` — `action` yeniden-yayını.
- **Modify:** `structured/view/structured-view.component.ts|html` — değişebilir tree + `graphChanged` + mutasyon uygulama + kök [+ ekle].
- **Modify:** `designer.component.html` — `(graphChanged)="onGraphChanged($event)"`.
- **Modify:** `public/assets/i18n/tr.json`, `en.json` — `structured.add/delete/moveUp/moveDown/activity/pickActivity`.

Ortak olay tipi (Task 3'te `structured-item.component.ts` içinde tanımlanır, diğerleri import eder):
```typescript
export type StructuredAction =
  | { kind: 'delete' | 'up' | 'down'; target: StructuredItem }
  | { kind: 'add'; container: ContainerItem; lane: LaneName; item: StructuredItem };
```

---

### Task 1: Saf `tree-ops` (mutasyonlar + findPath + yapıcılar)

**Files:**
- Create: `src/app/studio/designer/structured/edit/tree-ops.ts`
- Test: `src/app/studio/designer/structured/edit/tree-ops.spec.ts`

**Interfaces:**
- Consumes: `StructuredItem/StructuredSequence/StepItem/ContainerItem/ContainerType/LaneName/lanesFor` from `../structured-model`.
- Produces: `PathStep`, `Path`, `insertItem`, `removeItem`, `moveItem`, `findPath`, `newStep`, `newContainer`.

- [ ] **Step 1: Write the failing test**

```typescript
// tree-ops.spec.ts
import { insertItem, removeItem, moveItem, findPath, newStep, newContainer } from './tree-ops';
import { step, container, StructuredSequence } from '../structured-model';
import { WorkflowNode } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('tree-ops', () => {
  it('inserts into the root sequence at an index', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b'))];
    const out = insertItem(tree, [], 1, step(n('mid')));
    expect(out.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['a', 'mid', 'b']);
    expect(tree).toHaveLength(2); // immutable
  });

  it('inserts into a container lane', () => {
    const tree: StructuredSequence = [container('forEach', {}, { body: [step(n('x'))] })];
    const out = insertItem(tree, [{ lane: 'body', index: 0 }], 1, step(n('y')));
    const body = (out[0] as { lanes: { body: { node: WorkflowNode }[] } }).lanes.body;
    expect(body.map((i) => i.node.id)).toEqual(['x', 'y']);
  });

  it('removes an item by path', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b'))];
    const out = removeItem(tree, { steps: [], index: 0 });
    expect(out.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['b']);
  });

  it('moves an item within its sequence and is a no-op at bounds', () => {
    const tree: StructuredSequence = [step(n('a')), step(n('b')), step(n('c'))];
    const down = moveItem(tree, { steps: [], index: 0 }, 1);
    expect(down.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['b', 'a', 'c']);
    const noop = moveItem(tree, { steps: [], index: 0 }, -1);
    expect(noop.map((i) => (i as { node: WorkflowNode }).node.id)).toEqual(['a', 'b', 'c']);
  });

  it('finds the path of an item by reference (nested)', () => {
    const inner = step(n('inner'));
    const tree: StructuredSequence = [container('if', {}, { true: [inner], false: [] })];
    expect(findPath(tree, inner)).toEqual({ steps: [{ lane: 'true', index: 0 }], index: 0 });
    expect(findPath(tree, step(n('nope')))).toBeNull();
  });

  it('newStep/newContainer produce well-formed items', () => {
    const s = newStep('Web.Click');
    expect(s.kind).toBe('step');
    expect((s.node as WorkflowNode).activity).toBe('Web.Click');
    const c = newContainer('tryCatch');
    expect(c.kind).toBe('container');
    expect(Object.keys(c.lanes).sort()).toEqual(['failure', 'out', 'success']);
    expect(c.lanes.success).toEqual([]);
  });
});
```

- [ ] **Step 2: Run — expect FAIL (module not found)**

Run: `cd src/RPA.Studio && npx ng test --include="**/edit/tree-ops.spec.ts" --watch=false`

- [ ] **Step 3: Implement**

```typescript
// tree-ops.ts
import {
  ContainerItem, ContainerType, LaneName, StepItem, StructuredItem, StructuredSequence, lanesFor,
} from '../structured-model';

export interface PathStep { lane: LaneName; index: number; }
export interface Path { steps: PathStep[]; index: number; }

/** `steps` ile adreslenen alt-diziyi `fn` ile değiştirir (immutable). steps=[] → kök. */
function updateSeqAt(
  tree: StructuredSequence,
  steps: PathStep[],
  fn: (seq: StructuredSequence) => StructuredSequence,
): StructuredSequence {
  if (steps.length === 0) {
    return fn(tree);
  }
  const [head, ...rest] = steps;
  return tree.map((item, i) => {
    if (i !== head.index || item.kind !== 'container') {
      return item;
    }
    const lane = item.lanes[head.lane] ?? [];
    return { ...item, lanes: { ...item.lanes, [head.lane]: updateSeqAt(lane, rest, fn) } };
  });
}

export function insertItem(
  tree: StructuredSequence, seqSteps: PathStep[], index: number, item: StructuredItem,
): StructuredSequence {
  return updateSeqAt(tree, seqSteps, (seq) => [...seq.slice(0, index), item, ...seq.slice(index)]);
}

export function removeItem(tree: StructuredSequence, path: Path): StructuredSequence {
  return updateSeqAt(tree, path.steps, (seq) => seq.filter((_, i) => i !== path.index));
}

export function moveItem(tree: StructuredSequence, path: Path, delta: number): StructuredSequence {
  return updateSeqAt(tree, path.steps, (seq) => {
    const j = path.index + delta;
    if (j < 0 || j >= seq.length) {
      return seq;
    }
    const next = [...seq];
    const [moved] = next.splice(path.index, 1);
    next.splice(j, 0, moved);
    return next;
  });
}

/** Öğeyi referans eşitliğiyle ağaçta arar; path'ini döndürür (yoksa null). */
export function findPath(tree: StructuredSequence, target: StructuredItem): Path | null {
  const walk = (seq: StructuredSequence, steps: PathStep[]): Path | null => {
    for (let i = 0; i < seq.length; i++) {
      if (seq[i] === target) {
        return { steps, index: i };
      }
      const item = seq[i];
      if (item.kind === 'container') {
        for (const lane of lanesFor(item.type)) {
          const r = walk(item.lanes[lane] ?? [], [...steps, { lane, index: i }]);
          if (r) { return r; }
        }
      }
    }
    return null;
  };
  return walk(tree, []);
}

export function newStep(activityId: string): StepItem {
  return { kind: 'step', node: { id: crypto.randomUUID(), type: 'activity', activity: activityId } };
}

export function newContainer(type: ContainerType): ContainerItem {
  const lanes: Partial<Record<LaneName, StructuredSequence>> = {};
  for (const lane of lanesFor(type)) { lanes[lane] = []; }
  return { kind: 'container', type, props: {}, lanes };
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/edit/tree-ops.ts src/RPA.Studio/src/app/studio/designer/structured/edit/tree-ops.spec.ts
git commit -m "feat(studio): yapisal editor — saf tree-ops (insert/remove/move/findPath + yapicilar)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: `add-menu` bileşeni (kontrol tipleri + aktivite açılır listesi)

**Files:**
- Create: `structured/view/structured-add-menu.component.ts|html|scss`
- Test: `structured/view/structured-add-menu.component.spec.ts`

**Interfaces:**
- Consumes: `ActivityCatalogService` (`getActivities(): Observable<ActivityMetadata[]>`), `newStep`/`newContainer` (Task 1), `ContainerType`, `StructuredItem`.
- Produces: `StructuredAddMenuComponent` — `@Output() pick = EventEmitter<StructuredItem>()`.

- [ ] **Step 1: Write failing test**

```typescript
// structured-add-menu.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredAddMenuComponent } from './structured-add-menu.component';
import { ContainerItem } from '../structured-model';

describe('StructuredAddMenuComponent', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StructuredAddMenuComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('emits a container item when a control type is chosen', () => {
    const f = TestBed.createComponent(StructuredAddMenuComponent);
    f.componentInstance.open = true;
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([]));
    let picked: unknown;
    f.componentInstance.pick.subscribe((i) => (picked = i));
    (f.nativeElement.querySelector('[data-testid="add-type-if"]') as HTMLButtonElement).click();
    expect((picked as ContainerItem).type).toBe('if');
  });

  it('emits a step item when an activity is chosen', () => {
    const f = TestBed.createComponent(StructuredAddMenuComponent);
    f.componentInstance.open = true;
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    let picked: unknown;
    f.componentInstance.pick.subscribe((i) => (picked = i));
    f.componentInstance.chooseActivity('Web.Click');
    expect((picked as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement**

```typescript
// structured-add-menu.component.ts
import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../../shared/services/activity-catalog.service';
import { ActivityMetadata } from '../../../../shared/models/activity.model';
import { ContainerType, StructuredItem } from '../structured-model';
import { newContainer, newStep } from '../edit/tree-ops';

@Component({
  selector: 'app-structured-add-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './structured-add-menu.component.html',
  styleUrls: ['./structured-add-menu.component.scss'],
})
export class StructuredAddMenuComponent implements OnInit {
  private readonly catalog = inject(ActivityCatalogService);

  @Input() open = false;
  @Output() readonly pick = new EventEmitter<StructuredItem>();

  readonly controlTypes: ContainerType[] = ['if', 'forEach', 'for', 'while', 'tryCatch'];
  activities: ActivityMetadata[] = [];

  ngOnInit(): void {
    this.catalog.getActivities().subscribe({
      next: (a) => (this.activities = a),
      error: () => (this.activities = []),
    });
  }

  toggle(): void { this.open = !this.open; }

  chooseControl(type: ContainerType): void {
    this.pick.emit(newContainer(type));
    this.open = false;
  }

  chooseActivity(activityId: string): void {
    if (!activityId) { return; }
    this.pick.emit(newStep(activityId));
    this.open = false;
  }
}
```

```html
<!-- structured-add-menu.component.html -->
<div class="add-menu">
  <button type="button" class="add-menu__toggle" data-testid="add-toggle" (click)="toggle()">
    + {{ 'structured.add' | translate }}
  </button>
  @if (open) {
    <div class="add-menu__panel">
      @for (t of controlTypes; track t) {
        <button type="button" [attr.data-testid]="'add-type-' + t" (click)="chooseControl(t)">
          {{ 'structured.type.' + t | translate }}
        </button>
      }
      <label class="add-menu__activity">
        {{ 'structured.activity' | translate }}
        <select data-testid="add-activity" (change)="chooseActivity($any($event.target).value)">
          <option value="">{{ 'structured.pickActivity' | translate }}</option>
          @for (a of activities; track a.activityId) {
            <option [value]="a.activityId">{{ a.displayName || a.activityId }}</option>
          }
        </select>
      </label>
    </div>
  }
</div>
```

```scss
/* structured-add-menu.component.scss */
.add-menu { position: relative; display: inline-block; }
.add-menu__panel { display: flex; flex-wrap: wrap; gap: 4px; padding: 6px; border: 1px solid #cbd5e1; border-radius: 8px; background: #fff; margin-top: 4px; }
.add-menu__panel button { padding: 4px 8px; }
```

Not: `ActivityMetadata` alan adları (`activityId`, `displayName`) mevcut `shared/models/activity.model.ts` ile doğrulanmalı; farklıysa uyarlanır.

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-add-menu.component.*
git commit -m "feat(studio): yapisal editor — ekle menusu (kontrol tipleri + aktivite listesi)

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Öğe/dizi düzenleme denetimleri + olay köprüsü

**Files:**
- Modify: `structured/view/structured-item.component.ts|html`
- Modify: `structured/view/structured-sequence.component.ts|html`
- Test: `structured/view/structured-item.component.spec.ts`

**Interfaces:**
- Produces: `StructuredAction` tipi; `StructuredItemComponent.@Input() editable`, `@Output() action`;
  `StructuredSequenceComponent.@Input() editable`, `@Output() action`.

- [ ] **Step 1: Write failing test (item emits actions by reference)**

`structured-item.component.spec.ts` içine ekle:

```typescript
import { StructuredAction } from './structured-item.component';

it('emits delete/up/down actions carrying the item reference when editable', () => {
  const f = TestBed.createComponent(StructuredItemComponent);
  const item = step({ id: 's', type: 'activity', activity: 'A' });
  f.componentRef.setInput('item', item);
  f.componentRef.setInput('editable', true);
  f.detectChanges();
  const events: StructuredAction[] = [];
  f.componentInstance.action.subscribe((e) => events.push(e));
  (f.nativeElement.querySelector('[data-testid="item-delete"]') as HTMLButtonElement).click();
  expect(events[0]).toEqual({ kind: 'delete', target: item });
});

it('does not render edit controls when not editable', () => {
  const f = TestBed.createComponent(StructuredItemComponent);
  f.componentRef.setInput('item', step({ id: 's', type: 'activity', activity: 'A' }));
  f.componentRef.setInput('editable', false);
  f.detectChanges();
  expect(f.nativeElement.querySelector('[data-testid="item-delete"]')).toBeFalsy();
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement item component**

`structured-item.component.ts` — ortak tip + inputs/outputs ekle, `StructuredAddMenuComponent` import et:

```typescript
import { EventEmitter, Input, Output } from '@angular/core';
import { ContainerItem, LaneName, StructuredItem } from '../structured-model';
import { StructuredAddMenuComponent } from './structured-add-menu.component';

export type StructuredAction =
  | { kind: 'delete' | 'up' | 'down'; target: StructuredItem }
  | { kind: 'add'; container: ContainerItem; lane: LaneName; item: StructuredItem };
```

`@Component` `imports`'a `StructuredAddMenuComponent` ekle. Sınıfa:

```typescript
@Input() editable = false;
@Output() readonly action = new EventEmitter<StructuredAction>();

emitAction(kind: 'delete' | 'up' | 'down'): void {
  this.action.emit({ kind, target: this.item });
}
onLaneAdd(container: ContainerItem, lane: LaneName, item: StructuredItem): void {
  this.action.emit({ kind: 'add', container, lane, item });
}
```

`structured-item.component.html` — adım kartı ve konteyner başlığına denetimler; lane'lere ekle menüsü; çocuk `app-structured-item`'e `editable` + `(action)` köprüsü:

```html
@if (item.kind === 'step') {
  <div class="structured-step" data-testid="structured-step">
    <span class="structured-step__icon" aria-hidden="true">▸</span>
    <span class="structured-step__title">{{ stepTitle() }}</span>
    <span class="structured-step__type">{{ item.node.type }}</span>
    @if (editable) {
      <span class="structured-item__controls">
        <button type="button" data-testid="item-up" (click)="emitAction('up')">↑</button>
        <button type="button" data-testid="item-down" (click)="emitAction('down')">↓</button>
        <button type="button" data-testid="item-delete" (click)="emitAction('delete')">✕</button>
      </span>
    }
  </div>
} @else if (container) {
  <div class="structured-container" data-testid="structured-container" [attr.data-type]="container.type">
    <header class="structured-container__header">
      <span class="structured-container__label">{{ 'structured.type.' + container.type | translate }}</span>
      @if (summary(container)) { <code class="structured-container__summary">{{ summary(container) }}</code> }
      @if (editable) {
        <span class="structured-item__controls">
          <button type="button" data-testid="item-up" (click)="emitAction('up')">↑</button>
          <button type="button" data-testid="item-down" (click)="emitAction('down')">↓</button>
          <button type="button" data-testid="item-delete" (click)="emitAction('delete')">✕</button>
        </span>
      }
    </header>
    <div class="structured-container__lanes">
      @for (lane of lanes(container); track lane) {
        <section class="structured-container__lane" [attr.data-testid]="'lane-' + lane">
          <span class="structured-container__lane-label">{{ 'structured.lane.' + lane | translate }}</span>
          @for (child of laneItems(container, lane); track $index) {
            <app-structured-item [item]="child" [editable]="editable" (action)="action.emit($event)"></app-structured-item>
          }
          @if (laneItems(container, lane).length === 0) {
            <p class="structured-container__lane-empty" data-testid="lane-empty">{{ 'structured.empty' | translate }}</p>
          }
          @if (editable) {
            <app-structured-add-menu (pick)="onLaneAdd(container, lane, $event)"></app-structured-add-menu>
          }
        </section>
      }
    </div>
  </div>
}
```

- [ ] **Step 4: Implement sequence re-emission**

`structured-sequence.component.ts` — `StructuredItemComponent` zaten import; ekle:
```typescript
import { EventEmitter, Input, Output } from '@angular/core';
import { StructuredAction, StructuredItemComponent } from './structured-item.component';
// sınıfa:
@Input() editable = false;
@Output() readonly action = new EventEmitter<StructuredAction>();
```
`structured-sequence.component.html`:
```html
<div class="structured-sequence">
  @for (item of items; track $index) {
    <app-structured-item [item]="item" [editable]="editable" (action)="action.emit($event)"></app-structured-item>
  }
  @if (items.length === 0) {
    <p class="structured-sequence__empty" data-testid="sequence-empty">{{ 'structured.empty' | translate }}</p>
  }
</div>
```

- [ ] **Step 5: Run — expect PASS**

Run: `cd src/RPA.Studio && npx ng test --include="**/view/structured-item.component.spec.ts" --include="**/view/structured-sequence.component.spec.ts" --watch=false`

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-item.component.* src/RPA.Studio/src/app/studio/designer/structured/view/structured-sequence.component.*
git commit -m "feat(studio): yapisal editor — oge duzenleme denetimleri + referansli olay koprusu

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Düzenlenebilir host + mutasyon uygulama + designer bağlama + i18n

**Files:**
- Modify: `structured/view/structured-view.component.ts|html`
- Modify: `designer.component.html`
- Modify: `public/assets/i18n/tr.json`, `en.json`
- Test: `structured/view/structured-view.component.spec.ts`, `designer.component.spec.ts`

**Interfaces:**
- Consumes: `insertItem/removeItem/moveItem/findPath` (Task 1), `StructuredAction` (Task 3).
- Produces: `StructuredViewComponent` değişebilir `tree` signal + `@Output() graphChanged`.

- [ ] **Step 1: Write failing test**

`structured-view.component.spec.ts` içine ekle:

```typescript
it('applies a delete and emits an updated workflow', () => {
  const wf = treeToWorkflow([step(n('a')), step(n('b'))], { idGen: ids() });
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  let emitted: { nodes: unknown[] } | undefined;
  f.componentInstance.graphChanged.subscribe((g: never) => (emitted = g));
  // 'a' adımını sil
  const items = f.nativeElement.querySelectorAll('[data-testid="item-delete"]');
  (items[0] as HTMLButtonElement).click();
  expect(emitted).toBeTruthy();
  expect(emitted!.nodes.length).toBe(1);
});

it('does not render edit controls for a fallback workflow', () => {
  const wf = treeToWorkflow(
    [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
    { idGen: ids() },
  );
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf);
  f.detectChanges();
  expect(f.nativeElement.querySelector('[data-testid="item-delete"]')).toBeFalsy();
  expect(f.nativeElement.querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement host**

`structured-view.component.ts` — computed `state` yerine değişebilir tohumlama + mutasyon:

```typescript
import { EventEmitter, Output, signal } from '@angular/core';
import { StructuredItemComponent, StructuredAction } from './structured-item.component';
import { insertItem, removeItem, moveItem, findPath } from '../edit/tree-ops';
```

Sınıf gövdesini şu mantıkla değiştir (mevcut `convert` KORUNUR; sadece tohumlama + mutasyon eklenir):

```typescript
private seeded = false;
readonly tree = signal<StructuredSequence>([]);
readonly mode = signal<'empty' | 'tree' | 'fallback'>('empty');

@Input() set workflow(value: WorkflowVersion | null | undefined) {
  if (this.seeded) { return; }              // yalnız bir kez tohumla (echo'yu yok say)
  this.seeded = true;
  const s = this.convert(value ?? null);
  this.mode.set(s.kind);
  this.tree.set(s.tree ?? []);
}

@Output() readonly graphChanged = new EventEmitter<WorkflowVersion>();

get editable(): boolean { return this.mode() === 'tree'; }

onAction(a: StructuredAction): void {
  const t = this.tree();
  let next: StructuredSequence;
  if (a.kind === 'add') {
    const cp = findPath(t, a.container);
    if (!cp) { return; }
    const laneSteps = [...cp.steps, { lane: a.lane, index: cp.index }];
    const laneLen = (a.container.lanes[a.lane] ?? []).length;
    next = insertItem(t, laneSteps, laneLen, a.item);
  } else {
    const p = findPath(t, a.target);
    if (!p) { return; }
    next = a.kind === 'delete' ? removeItem(t, p)
      : moveItem(t, p, a.kind === 'up' ? -1 : 1);
  }
  this.tree.set(next);
  this.graphChanged.emit(treeToWorkflow(next));
}

addToRoot(item: StructuredItem): void {
  const next = insertItem(this.tree(), [], this.tree().length, item);
  this.tree.set(next);
  this.graphChanged.emit(treeToWorkflow(next));
}
```

`convert` mevcut haliyle kalır ama artık `{ kind, tree }` döndürdüğü için imzası uyumlu (B'de `ViewState` zaten öyle). `imports`'a `StructuredItemComponent` gerekmez (sequence kullanılıyor); `StructuredAddMenuComponent`'i kök [+ ekle] için ekle.

`structured-view.component.html` — `tree` dalını düzenlenebilir yap:

```html
    @case ('tree') {
      <div class="structured-view__toolbar"> … zoom (mevcut) … </div>
      <div #scroll class="structured-view__scroll" (wheel)="onWheel($event)"
           (pointerdown)="onPanStart($event, scroll)" (pointermove)="onPanMove($event, scroll)"
           (pointerup)="onPanEnd()" (pointerleave)="onPanEnd()">
        <div class="structured-view__canvas" data-testid="structured-view-tree" [style.transform]="'scale(' + zoom() + ')'">
          <app-structured-sequence [items]="tree()" [editable]="editable" (action)="onAction($event)"></app-structured-sequence>
          @if (editable) {
            <app-structured-add-menu data-testid="root-add" (pick)="addToRoot($event)"></app-structured-add-menu>
          }
        </div>
      </div>
    }
```
(`@switch (state().kind)` yerine `@switch (mode())`; `state()` kaldırılır.)

- [ ] **Step 4: Designer bağlama + i18n**

`designer.component.html`:
```html
<app-structured-view [workflow]="currentGraph() ?? workflow()" (graphChanged)="onGraphChanged($event)"></app-structured-view>
```

i18n `tr.json` `structured` bloğuna ekle: `"add":"Ekle","delete":"Sil","moveUp":"Yukarı","moveDown":"Aşağı","activity":"Aktivite","pickActivity":"Aktivite seç…"`.
`en.json`: `"add":"Add","delete":"Delete","moveUp":"Up","moveDown":"Down","activity":"Activity","pickActivity":"Pick activity…"`.

- [ ] **Step 5: Designer test**

`designer.component.spec.ts` structured-view describe'ına ekle:
```typescript
it('marks dirty and updates currentGraph when structured view emits graphChanged', () => {
  const fixture = TestBed.createComponent(DesignerComponent);
  const cmp = fixture.componentInstance;
  fixture.detectChanges();
  cmp.toggleStructuredView();
  fixture.detectChanges();
  const g = { schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0', nodes: [], connections: [] };
  cmp.onGraphChanged(g as never);
  expect(cmp.dirty()).toBe(true);
  expect(cmp.currentGraph()).toEqual(g);
});
```

- [ ] **Step 6: Run — expect PASS**

Run: `cd src/RPA.Studio && npx ng test --include="**/view/structured-view.component.spec.ts" --include="**/designer.component.spec.ts" --watch=false`

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.* src/RPA.Studio/src/app/studio/designer/designer.component.html src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts src/RPA.Studio/public/assets/i18n/tr.json src/RPA.Studio/public/assets/i18n/en.json
git commit -m "feat(studio): yapisal editor — degisebilir tree kaynak + mutasyon + taslak kalicilik

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

Yapısal görünüme geç (yapısal alt-kümeden bir workflow ya da boş taslak), kök [+ ekle] ile bir "Her Biri İçin" ekle, body lane'ine [+ ekle] ile bir aktivite ekle, [↑]/[↓] ile sırala, [✕] ile sil — her mutasyonda designer "dirty" olur ve kaydedilebilir. Fallback workflow'da düzenleme denetimleri görünmez.

- [ ] **Step 5: Commit (gerekirse)**

```bash
git add -A
git commit -m "test(studio): yapisal editor C1 tam paket dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
