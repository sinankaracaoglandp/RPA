# Yapısal Konteyner Editörü — Alt-proje B (Render + Salt-Okunur Görünüm) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A'nın ürettiği `StructuredSequence`'i iç içe kutular + lane'ler olarak özyinelemeli Angular + CSS ile salt-okunur render etmek ve designer'a bir "Yapısal görünüm" toggle'ı eklemek (kaydırma + pan + zoom gezinme, çevrilemeyen workflow'da fallback).

**Architecture:** Üç özyinelemeli standalone bileşen (`structured/view/`): host (`StructuredViewComponent`) workflow'u A ile ağaca çevirir + güvence/fallback + gezinme kabuğu; `StructuredSequenceComponent` diziyi dikey render eder; `StructuredItemComponent` adım kartını ya da lane'li konteyner kutusunu render eder (lane'ler yine sequence → özyineleme). Yerleşim tamamen CSS akışı. Runtime/kontrat değişmez.

**Tech Stack:** Angular standalone components (signals, OnPush), CSS flex, Vitest (`ng test --include=<spec> --watch=false`).

## Global Constraints

- Salt-okunur: düzenleme/sürükle-bırak/otomatik-tel/seçme yok (C).
- Rete yok, elle koordinat yok, SVG yok; yerleşim CSS akışı. Yeni bağımlılık yok.
- A modülleri (`../structured-model`, `../workflow-to-tree`, `../tree-to-workflow`, `../structural-invariants`) değişmez; B onları tüketir.
- **Import derinliği:** `structured/view/` bir seviye daha derindir. `structured-model` vb. için `'../structured-model'` (bir üst); `shared/models/workflow.model` için `'../../../../shared/models/workflow.model'` (dört üst). (Not: A dosyaları `structured/` içindeydi ve `'../../../shared/...'` kullanıyordu; view bir seviye daha derinde → dört nokta.)
- i18n anahtarları hem `public/assets/i18n/tr.json` hem `en.json`'a eklenir.
- Test komutu: `cd src/RPA.Studio && npx ng test --include="**/<spec>" --watch=false`.

---

## Dosya Yapısı

- **Create:** `src/app/studio/designer/structured/view/structured-item.component.ts|html|scss` + spec.
- **Create:** `src/app/studio/designer/structured/view/structured-sequence.component.ts|html|scss` + spec.
- **Create:** `src/app/studio/designer/structured/view/structured-view.component.ts|html|scss` + spec.
- **Modify:** `src/app/studio/designer/designer.component.ts|html` — `structuredView` signal + toggle + koşullu görünüm.
- **Modify:** `public/assets/i18n/tr.json`, `public/assets/i18n/en.json` — `structured.*` anahtarları.

Interface sözleşmeleri:
- `StructuredSequenceComponent`: `@Input() items: StructuredSequence`.
- `StructuredItemComponent`: `@Input() item: StructuredItem`.
- `StructuredViewComponent`: `@Input() workflow: WorkflowVersion | null`.

---

### Task 1: Özyinelemeli renderer (`StructuredItemComponent` + `StructuredSequenceComponent`)

**Files:**
- Create: `.../view/structured-sequence.component.ts|html`
- Create: `.../view/structured-item.component.ts|html|scss`
- Create: `.../view/structured-sequence.component.spec.ts`, `.../view/structured-item.component.spec.ts`

**Interfaces:**
- Consumes: `StructuredItem`, `StructuredSequence`, `ContainerItem`, `lanesFor`, `LaneName` from `../structured-model`.
- Produces: `StructuredSequenceComponent`, `StructuredItemComponent` (karşılıklı özyineleme).

- [ ] **Step 1: Write failing tests**

```typescript
// structured-item.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { StructuredItemComponent } from './structured-item.component';
import { step, container } from '../structured-model';

describe('StructuredItemComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StructuredItemComponent] }));

  it('renders a step card with title and activity id', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 'n1', type: 'activity', activity: 'Web.Click' }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-step"]')).toBeTruthy();
    expect(el.textContent).toContain('Web.Click');
  });

  it('renders a container box with a type label and lane sections', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('if', { condition: '{{c}} == 1' }, {
      true: [step({ id: 't', type: 'activity', activity: 'A' })], false: [],
    }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-container"]')).toBeTruthy();
    // lane etiketleri i18n anahtarı olarak render edilir (TranslatePipe test ortamında anahtarı döner)
    expect(el.querySelector('[data-testid="lane-true"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="lane-false"]')).toBeTruthy();
  });
});
```

```typescript
// structured-sequence.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { StructuredSequenceComponent } from './structured-sequence.component';
import { step } from '../structured-model';

describe('StructuredSequenceComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StructuredSequenceComponent] }));

  it('renders one item element per sequence entry', () => {
    const f = TestBed.createComponent(StructuredSequenceComponent);
    f.componentRef.setInput('items', [
      step({ id: 'a', type: 'activity', activity: 'A' }),
      step({ id: 'b', type: 'activity', activity: 'B' }),
    ]);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelectorAll('app-structured-item').length).toBe(2);
  });

  it('shows an empty hint for an empty sequence', () => {
    const f = TestBed.createComponent(StructuredSequenceComponent);
    f.componentRef.setInput('items', []);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="sequence-empty"]')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run — expect FAIL (module not found)**

Run: `cd src/RPA.Studio && npx ng test --include="**/view/structured-item.component.spec.ts" --include="**/view/structured-sequence.component.spec.ts" --watch=false`

- [ ] **Step 3: Implement the sequence component**

```typescript
// structured-sequence.component.ts
import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { StructuredSequence } from '../structured-model';
import { StructuredItemComponent } from './structured-item.component';

@Component({
  selector: 'app-structured-sequence',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredItemComponent],
  templateUrl: './structured-sequence.component.html',
})
export class StructuredSequenceComponent {
  @Input() items: StructuredSequence = [];
}
```

```html
<!-- structured-sequence.component.html -->
<div class="structured-sequence">
  @for (item of items; track $index) {
    <app-structured-item [item]="item"></app-structured-item>
  }
  @if (items.length === 0) {
    <p class="structured-sequence__empty" data-testid="sequence-empty">{{ 'structured.empty' | translate }}</p>
  }
</div>
```

- [ ] **Step 4: Implement the item component**

```typescript
// structured-item.component.ts
import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ContainerItem, LaneName, StructuredItem, lanesFor } from '../structured-model';
import { StructuredSequenceComponent } from './structured-sequence.component';

@Component({
  selector: 'app-structured-item',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredSequenceComponent],
  templateUrl: './structured-item.component.html',
  styleUrls: ['./structured-item.component.scss'],
})
export class StructuredItemComponent {
  @Input({ required: true }) item!: StructuredItem;

  get container(): ContainerItem | null {
    return this.item.kind === 'container' ? this.item : null;
  }

  lanes(c: ContainerItem): LaneName[] {
    return lanesFor(c.type);
  }

  laneItems(c: ContainerItem, lane: LaneName) {
    return c.lanes[lane] ?? [];
  }

  /** Konteyner başlığındaki kısa props özeti. */
  summary(c: ContainerItem): string {
    const p = c.props;
    switch (c.type) {
      case 'forEach': return String(p['items'] ?? '');
      case 'for': return `${p['start'] ?? ''}..${p['end'] ?? ''}`;
      case 'while':
      case 'if': return String(p['condition'] ?? '');
      case 'tryCatch': return String(p['exceptionVariable'] ?? '');
      default: return '';
    }
  }

  stepTitle(): string {
    if (this.item.kind !== 'step') { return ''; }
    return this.item.node.activity ?? this.item.node.type;
  }
}
```

```html
<!-- structured-item.component.html -->
@if (item.kind === 'step') {
  <div class="structured-step" data-testid="structured-step">
    <span class="structured-step__icon" aria-hidden="true">▸</span>
    <span class="structured-step__title">{{ stepTitle() }}</span>
    <span class="structured-step__type">{{ item.node.type }}</span>
  </div>
} @else if (container) {
  <div class="structured-container" data-testid="structured-container" [attr.data-type]="container.type">
    <header class="structured-container__header">
      <span class="structured-container__label">{{ 'structured.type.' + container.type | translate }}</span>
      @if (summary(container)) {
        <code class="structured-container__summary">{{ summary(container) }}</code>
      }
    </header>
    <div class="structured-container__lanes">
      @for (lane of lanes(container); track lane) {
        <section class="structured-container__lane" [attr.data-testid]="'lane-' + lane">
          <span class="structured-container__lane-label">{{ 'structured.lane.' + lane | translate }}</span>
          <app-structured-sequence [items]="laneItems(container, lane)"></app-structured-sequence>
        </section>
      }
    </div>
  </div>
}
```

```scss
/* structured-item.component.scss */
.structured-step {
  display: flex; align-items: center; gap: 8px;
  padding: 8px 12px; border: 1px solid var(--border, #cbd5e1); border-radius: 8px;
  background: #fff;
  &__title { font-weight: 600; }
  &__type { margin-left: auto; font-size: 12px; color: #64748b; }
}
.structured-container {
  border: 1px solid var(--border, #cbd5e1); border-radius: 10px; background: #f8fafc;
  &__header { display: flex; align-items: center; gap: 8px; padding: 8px 12px; border-bottom: 1px solid #e2e8f0; }
  &__label { font-weight: 700; }
  &__summary { font-size: 12px; color: #475569; }
  &__lanes { display: flex; flex-direction: column; gap: 8px; padding: 8px; }
  &__lane { border-left: 3px solid #94a3b8; padding-left: 10px; }
  &__lane-label { display: block; font-size: 12px; font-weight: 600; color: #475569; margin-bottom: 4px; }
}
```

- [ ] **Step 5: Add i18n keys**

`public/assets/i18n/tr.json` ve `en.json`'a `structured` bloğu ekle (mevcut `foreach` bloğunun yanına):

tr:
```json
"structured": {
  "empty": "boş",
  "toggle": "Yapısal görünüm",
  "fallback": "Bu workflow yapısal görünüme uygun değil (serbest-graf / tryCatch — Faz C/D).",
  "emptyView": "Görüntülenecek adım yok.",
  "type": { "forEach": "Her Biri İçin", "for": "Sayaç Döngüsü", "while": "While", "if": "Eğer", "tryCatch": "Dene-Yakala" },
  "lane": { "body": "Gövde", "true": "Doğru", "false": "Yanlış", "success": "Dene", "failure": "Yakala", "out": "Finally" }
}
```
en:
```json
"structured": {
  "empty": "empty",
  "toggle": "Structured view",
  "fallback": "This workflow is not structured-viewable (free-graph / tryCatch — Phase C/D).",
  "emptyView": "No steps to display.",
  "type": { "forEach": "For Each", "for": "Counter Loop", "while": "While", "if": "If", "tryCatch": "Try-Catch" },
  "lane": { "body": "Body", "true": "True", "false": "False", "success": "Try", "failure": "Catch", "out": "Finally" }
}
```

- [ ] **Step 6: Run — expect PASS**

Run: `cd src/RPA.Studio && npx ng test --include="**/view/structured-item.component.spec.ts" --include="**/view/structured-sequence.component.spec.ts" --watch=false`

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-sequence.component.* src/RPA.Studio/src/app/studio/designer/structured/view/structured-item.component.* src/RPA.Studio/public/assets/i18n/tr.json src/RPA.Studio/public/assets/i18n/en.json
git commit -m "feat(studio): yapisal gorunum — ozyinelemeli adim/konteyner renderer

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: Host bileşeni (`StructuredViewComponent`) — dönüşüm + güvence + fallback

**Files:**
- Create: `.../view/structured-view.component.ts|html|scss`
- Test: `.../view/structured-view.component.spec.ts`

**Interfaces:**
- Consumes: `workflowToTree` (`../workflow-to-tree`), `treeToWorkflow` (`../tree-to-workflow`), `checkStructuralInvariants` (`../structural-invariants`), `StructuredSequence`, `WorkflowVersion`.
- Produces: `StructuredViewComponent` (`@Input() workflow: WorkflowVersion | null`), `tree`/`fallback` durumları.

- [ ] **Step 1: Write failing test**

```typescript
// structured-view.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredViewComponent } from './structured-view.component';
import { treeToWorkflow } from '../tree-to-workflow';
import { step, container } from '../structured-model';
import { WorkflowNode } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

describe('StructuredViewComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [StructuredViewComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  it('renders the structured tree for a structural-subset workflow', () => {
    const wf = treeToWorkflow([
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] }),
      step(n('after')),
    ], { idGen: (() => { let i = 0; return () => `c${++i}`; })() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-view-tree"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="structured-container"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="structured-view-fallback"]')).toBeFalsy();
  });

  it('shows the fallback when conversion throws (tryCatch)', () => {
    const wf = treeToWorkflow(
      [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
      { idGen: (() => { let i = 0; return () => `c${++i}`; })() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
  });

  it('shows the fallback for a non-structural free-graph', () => {
    // İki entry / yapısal olmayan: guard round-trip eşleşmez → fallback.
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
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
  });

  it('shows an empty state for a null workflow', () => {
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', null);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="structured-view-empty"]')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement the host**

```typescript
// structured-view.component.ts
import { ChangeDetectionStrategy, Component, Input, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVersion } from '../../../../shared/models/workflow.model';
import { StructuredSequence } from '../structured-model';
import { workflowToTree } from '../workflow-to-tree';
import { treeToWorkflow } from '../tree-to-workflow';
import { checkStructuralInvariants } from '../structural-invariants';
import { StructuredSequenceComponent } from './structured-sequence.component';

interface ViewState {
  kind: 'empty' | 'tree' | 'fallback';
  tree?: StructuredSequence;
}

@Component({
  selector: 'app-structured-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredSequenceComponent],
  templateUrl: './structured-view.component.html',
  styleUrls: ['./structured-view.component.scss'],
})
export class StructuredViewComponent {
  private readonly _workflow = signal<WorkflowVersion | null>(null);
  @Input() set workflow(value: WorkflowVersion | null) { this._workflow.set(value); }

  readonly state = computed<ViewState>(() => this.convert(this._workflow()));

  private convert(workflow: WorkflowVersion | null): ViewState {
    if (!workflow || workflow.nodes.length === 0) {
      return { kind: 'empty' };
    }
    try {
      const tree = workflowToTree(workflow);
      // Güvence: graf yapısal alt-küme değilse (keyfi serbest-graf) fallback.
      // 1) Girişin kendisi yapısal değişmezleri ihlal ediyorsa (ör. eksik loop-back) → fallback.
      if (checkStructuralInvariants(workflow).length > 0) {
        return { kind: 'fallback' };
      }
      // 2) Ağacı geri çevir; node/bağlantı SAYILARI eşleşmiyorsa (ör. düşen dallar, ekstra entry)
      //    graf sadık biçimde temsil edilememiştir → fallback. (id'ler farklı olduğundan yalnız
      //    sayı karşılaştırılır; kanonik eşitlik D'de.)
      let i = 0;
      const back = treeToWorkflow(tree, { idGen: () => `g${++i}` });
      if (back.nodes.length !== workflow.nodes.length
        || back.connections.length !== workflow.connections.length) {
        return { kind: 'fallback' };
      }
      return { kind: 'tree', tree };
    } catch {
      return { kind: 'fallback' };
    }
  }
}
```

> **Not (guard):** `treeToWorkflow`'un ürettiği id'ler girişten farklı olduğundan kanonik eşitlik
> yerine guard iki id-bağımsız ölçüt kullanır: (1) girişin yapısal değişmezleri (`checkStructuralInvariants`)
> ve (2) geri-çevrimde node + bağlantı **sayısı** eşitliği. Yapısal-olmayan graf ya değişmezleri
> ihlal eder ya da farklı sayı üretir (ör. entry'den ulaşılamayan node düşer) → fallback. Bu, keyfi
> serbest-grafın sessizce yanlış render edilmesini önlemek için yeterlidir; tam kanonik karşılaştırma D'de.

- [ ] **Step 4: Implement host template + scss (navigation Task 3'te eklenecek; şimdilik statik)**

```html
<!-- structured-view.component.html -->
<div class="structured-view" data-testid="structured-view">
  @switch (state().kind) {
    @case ('empty') {
      <p class="structured-view__msg" data-testid="structured-view-empty">{{ 'structured.emptyView' | translate }}</p>
    }
    @case ('fallback') {
      <p class="structured-view__msg" data-testid="structured-view-fallback">{{ 'structured.fallback' | translate }}</p>
    }
    @case ('tree') {
      <div class="structured-view__scroll">
        <div class="structured-view__canvas" data-testid="structured-view-tree">
          <app-structured-sequence [items]="state().tree ?? []"></app-structured-sequence>
        </div>
      </div>
    }
  }
</div>
```

```scss
/* structured-view.component.scss */
.structured-view { height: 100%; width: 100%; }
.structured-view__scroll { height: 100%; overflow: auto; }
.structured-view__canvas { transform-origin: top left; padding: 16px; width: max-content; min-width: 100%; }
.structured-view__msg { padding: 24px; color: #64748b; }
```

- [ ] **Step 5: Run — expect PASS** (guard yeterli değilse yukarıdaki nota göre sadeleştir)

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.*
git commit -m "feat(studio): yapisal gorunum host — donusum + round-trip guvence + fallback

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Gezinme — sürükle-pan + zoom

**Files:**
- Modify: `.../view/structured-view.component.ts|html|scss`
- Test: `.../view/structured-view.component.spec.ts`

**Interfaces:**
- Produces: `zoom` signal, `zoomIn()/zoomOut()`, `onWheel()`, pan `pointerdown/move/up` işleyicileri.

- [ ] **Step 1: Write failing test**

```typescript
// structured-view.component.spec.ts içine ekle
it('zoom in/out changes the zoom factor within clamp bounds', () => {
  const wf = { schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
    nodes: [{ id: 'a', type: 'activity', activity: 'X' }], connections: [] };
  const f = TestBed.createComponent(StructuredViewComponent);
  f.componentRef.setInput('workflow', wf as never);
  f.detectChanges();
  const cmp = f.componentInstance;
  const start = cmp.zoom();
  cmp.zoomIn();
  expect(cmp.zoom()).toBeGreaterThan(start);
  for (let i = 0; i < 20; i++) { cmp.zoomIn(); }
  expect(cmp.zoom()).toBeLessThanOrEqual(2);
  for (let i = 0; i < 40; i++) { cmp.zoomOut(); }
  expect(cmp.zoom()).toBeGreaterThanOrEqual(0.4);
});
```

- [ ] **Step 2: Run — expect FAIL (`cmp.zoom is not a function`)**

- [ ] **Step 3: Implement zoom + pan in the host**

`structured-view.component.ts` sınıfına ekle:

```typescript
readonly zoom = signal(1);
private static readonly ZOOM_MIN = 0.4;
private static readonly ZOOM_MAX = 2;
private static readonly ZOOM_STEP = 1.15;

private clampZoom(z: number): number {
  return Math.min(StructuredViewComponent.ZOOM_MAX, Math.max(StructuredViewComponent.ZOOM_MIN, z));
}
zoomIn(): void { this.zoom.update((z) => this.clampZoom(z * StructuredViewComponent.ZOOM_STEP)); }
zoomOut(): void { this.zoom.update((z) => this.clampZoom(z / StructuredViewComponent.ZOOM_STEP)); }

onWheel(event: WheelEvent): void {
  if (!event.ctrlKey) { return; }
  event.preventDefault();
  if (event.deltaY < 0) { this.zoomIn(); } else { this.zoomOut(); }
}

// Pan: boş alanda sürükleyerek scroll konumunu değiştir.
private panning = false;
private panX = 0; private panY = 0; private scrollX = 0; private scrollY = 0;
onPanStart(event: PointerEvent, scroll: HTMLElement): void {
  if (event.button !== 0) { return; }
  this.panning = true;
  this.panX = event.clientX; this.panY = event.clientY;
  this.scrollX = scroll.scrollLeft; this.scrollY = scroll.scrollTop;
}
onPanMove(event: PointerEvent, scroll: HTMLElement): void {
  if (!this.panning) { return; }
  scroll.scrollLeft = this.scrollX - (event.clientX - this.panX);
  scroll.scrollTop = this.scrollY - (event.clientY - this.panY);
}
onPanEnd(): void { this.panning = false; }
```

- [ ] **Step 4: Wire template (zoom transform + controls + pan handlers)**

`structured-view.component.html` `tree` dalını güncelle:

```html
    @case ('tree') {
      <div class="structured-view__toolbar">
        <button type="button" data-testid="structured-zoom-out" (click)="zoomOut()">−</button>
        <span class="structured-view__zoom">{{ (zoom() * 100) | number:'1.0-0' }}%</span>
        <button type="button" data-testid="structured-zoom-in" (click)="zoomIn()">+</button>
      </div>
      <div
        #scroll
        class="structured-view__scroll"
        (wheel)="onWheel($event)"
        (pointerdown)="onPanStart($event, scroll)"
        (pointermove)="onPanMove($event, scroll)"
        (pointerup)="onPanEnd()"
        (pointerleave)="onPanEnd()"
      >
        <div
          class="structured-view__canvas"
          data-testid="structured-view-tree"
          [style.transform]="'scale(' + zoom() + ')'"
        >
          <app-structured-sequence [items]="state().tree ?? []"></app-structured-sequence>
        </div>
      </div>
    }
```

`CommonModule` zaten import'lu (`number` pipe için yeterli). scss'e küçük toolbar stili ekle:

```scss
.structured-view__toolbar { display: flex; align-items: center; gap: 8px; padding: 6px 12px; border-bottom: 1px solid #e2e8f0; }
.structured-view__scroll { height: calc(100% - 37px); overflow: auto; cursor: grab; }
```

- [ ] **Step 5: Run — expect PASS**

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.*
git commit -m "feat(studio): yapisal gorunum gezinme — surukle-pan + Ctrl/wheel + zoom butonlari

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Designer entegrasyonu — toggle + koşullu görünüm

**Files:**
- Modify: `src/app/studio/designer/designer.component.ts` (import + signal + toggle)
- Modify: `src/app/studio/designer/designer.component.html` (buton + koşullu `app-canvas`/`app-structured-view`)
- Test: `src/app/studio/designer/designer.component.spec.ts`

**Interfaces:**
- Consumes: `StructuredViewComponent`.
- Produces: `DesignerComponent.structuredView` signal, `toggleStructuredView()`.

- [ ] **Step 1: Write failing test**

`designer.component.spec.ts` (ForEach injection describe'ının yanına yeni describe) ekle:

```typescript
describe('DesignerComponent — structured view toggle', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DesignerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    }).compileComponents();
  });

  it('toggles between canvas and structured view', () => {
    const fixture = TestBed.createComponent(DesignerComponent);
    const cmp = fixture.componentInstance;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-canvas')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-structured-view')).toBeFalsy();

    cmp.toggleStructuredView();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-structured-view')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-canvas')).toBeFalsy();
  });

  afterEach(() => {
    (TestBed.inject(HttpTestingController)).match('/api/activities').forEach((r) => r.flush([]));
  });
});
```

(Üst importlara `HttpTestingController` zaten var; yoksa ekle.)

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement in designer.component.ts**

Import ekle:
```typescript
import { StructuredViewComponent } from './structured/view/structured-view.component';
```
`imports` dizisine `StructuredViewComponent` ekle. Sınıfa ekle:
```typescript
readonly structuredView = signal(false);
toggleStructuredView(): void { this.structuredView.update((v) => !v); }
```

- [ ] **Step 4: Implement in designer.component.html**

Header'a (Konsol düğmesinin yanına, `</div>` header kapanışından önce) toggle:
```html
      <button
        type="button"
        class="designer__structured-toggle inline-flex min-h-8 items-center justify-center rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm font-semibold leading-none text-slate-700 shadow-sm transition hover:border-slate-400 hover:bg-slate-50"
        data-testid="designer-structured-toggle"
        [attr.aria-pressed]="structuredView()"
        (click)="toggleStructuredView()"
      >
        {{ 'structured.toggle' | translate }}
      </button>
```

`app-canvas`'ı koşullu sar:
```html
    @if (structuredView()) {
      <app-structured-view [workflow]="currentGraph() ?? workflow()"></app-structured-view>
    } @else {
      <app-canvas
        [workflow]="workflow()"
        [breakpointNodeIds]="breakpointNodeIds()"
        [bodyHighlightNodeIds]="loopBodyHighlightIds()"
        [currentNodeId]="debugCurrentNodeId()"
        (nodeSelect)="onNodeSelect($event)"
        (graphChanged)="onGraphChanged($event)"
      ></app-canvas>
    }
```

- [ ] **Step 5: Run — expect PASS**

Run: `cd src/RPA.Studio && npx ng test --include="**/designer.component.spec.ts" --watch=false`

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/designer.component.ts src/RPA.Studio/src/app/studio/designer/designer.component.html src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts
git commit -m "feat(studio): designer'a salt-okunur yapisal gorunum toggle'i

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: Tam test + build doğrulaması

- [ ] **Step 1: Structured view specs**

Run: `cd src/RPA.Studio && npx ng test --include="**/view/*.spec.ts" --watch=false`
Expected: yeşil.

- [ ] **Step 2: Full suite**

Run: `cd src/RPA.Studio && npx ng test --watch=false`
Expected: tümü yeşil (yeni + mevcut).

- [ ] **Step 3: Build**

Run: `cd src/RPA.Studio && npx ng build 2>&1 | tail -20`
Expected: yeni `structured/view/` kodundan TS hatası YOK. (`einvoice-mapping-editor.component.scss` bütçe hatası önceden var olan, ilgisiz.)

- [ ] **Step 4: Manuel doğrulama (verify skill)**

Designer'ı aç, yapısal alt-kümeden bir workflow (ör. bir ForEach + gövde) kur, "Yapısal görünüm"e bas → iç içe kutu render; zoom +/− ve sürükle-pan çalışır. tryCatch içeren workflow'da fallback mesajı görünür.

- [ ] **Step 5: Commit (gerekirse)**

```bash
git add -A
git commit -m "test(studio): yapisal gorunum alt-proje B tam paket dogrulamasi

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
