# Yapısal Palet Tıkla-Ekle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yapısal görünümdeki palet çiplerine çift tık veya çipin sağ üst köşesindeki `+` düğmesiyle, sürüklemeden node eklenebilmesi.

**Architecture:** `StructuredPaletteComponent` yeni bir `add` output'u yayınlar (yalnız "hangi node" bilgisi — yerleştirme mantığı içermez). `StructuredViewComponent` bunu `addFromPalette(item)` ile karşılar ve **kural C** ile yerleştirir: seçili öğe varsa aynı dizide `p.index + 1`, yoksa kökün sonuna. Mevcut `commit()` yolu kullanıldığı için undo/redo ve `graphChanged` bedava gelir.

**Tech Stack:** Angular 17+ standalone components, signals, Angular CDK drag-drop, Jasmine/Karma.

**Spec:** `docs/superpowers/specs/2026-07-21-structured-palette-click-add-design.md`

## Global Constraints

- Sürükle-bırak yolu (`cdkDrag` + `[cdkDragData]="{ factory: c.factory }"`) **aynen korunur** — mevcut testler kırılmamalıdır.
- Kural C: ekleme her zaman **kardeş seviyesinde** yapılır; konteyner seçiliyken içine değil ardına eklenir.
- Kontrat değişikliği yoktur — `src/RPA.Domain`, `src/RPA.Infrastructure`, `src/RPA.WebAPI`, `src/RPA.Agent` **dosyalarına dokunulmaz**.
- i18n anahtarları hem `src/RPA.Studio/public/assets/i18n/tr.json` hem `en.json` içine eklenir.
- Test komutu (repo kökünden): `cd src/RPA.Studio && npx ng test --watch=false --browsers=ChromeHeadless`

---

### Task 1: Palette `add` output — çift tık ve `+` düğmesi

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.scss`
- Modify: `src/RPA.Studio/public/assets/i18n/tr.json`
- Modify: `src/RPA.Studio/public/assets/i18n/en.json`
- Test: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.spec.ts`

**Interfaces:**
- Consumes: mevcut `Chip { label, category, factory: () => StructuredItem }` ve `ControlChip extends Chip { type: ContainerType }` (aynı dosyada tanımlı, değişmez).
- Produces: `StructuredPaletteComponent.add: EventEmitter<StructuredItem>` ve `emitAdd(chip: Chip): void`. Task 2 bunu template'te `(add)="addFromPalette($event)"` ile bağlar.

- [ ] **Step 1: Write the failing test**

`structured-palette.component.spec.ts` içine, mevcut `describe('StructuredPaletteComponent', ...)` bloğunun sonuna ekle:

```ts
  it('emits add when a chip is double-clicked', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
    ]));
    f.detectChanges();

    const emitted: StructuredItem[] = [];
    f.componentInstance.add.subscribe((i: StructuredItem) => emitted.push(i));

    const chipEl = (f.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('[data-testid="palette-chip-Tıkla"]')!;
    chipEl.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));

    expect(emitted.length).toBe(1);
    expect((emitted[0] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('emits add when the chip + button is clicked', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([]));
    f.detectChanges();

    const emitted: StructuredItem[] = [];
    f.componentInstance.add.subscribe((i: StructuredItem) => emitted.push(i));

    const plus = (f.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('[data-testid="palette-add-if"]')!;
    plus.click();

    expect(emitted.length).toBe(1);
    expect((emitted[0] as ContainerItem).type).toBe('if');
  });

  it('does not let the + button start a drag', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([]));
    f.detectChanges();

    const plus = (f.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('[data-testid="palette-add-if"]')!;
    const ev = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    const stop = spyOn(ev, 'stopPropagation');
    plus.dispatchEvent(ev);

    expect(stop).toHaveBeenCalled();
  });
```

Aynı dosyanın en üstündeki import satırını şununla değiştir (yalnız `StructuredItem` eklenir):

```ts
import { ContainerItem, StructuredItem } from '../structured-model';
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx ng test --watch=false --browsers=ChromeHeadless --include='**/structured-palette.component.spec.ts'`
Expected: FAIL — `f.componentInstance.add` tanımsız olduğu için `Cannot read properties of undefined (reading 'subscribe')`.

- [ ] **Step 3: Write minimal implementation**

`structured-palette.component.ts` — import satırına `EventEmitter` ve `Output` ekle:

```ts
import { ChangeDetectionStrategy, Component, EventEmitter, OnInit, Output, inject } from '@angular/core';
```

Sınıfın içine, `activityChips: Chip[] = [];` satırının hemen ardına ekle:

```ts
  /**
   * Sürüklemeden ekleme: çipe çift tık ya da çipin `+` düğmesi. Palet yalnız "hangi node"
   * bilgisini yayınlar; yerleştirme mantığı StructuredViewComponent'e aittir (kural C).
   */
  @Output() readonly add = new EventEmitter<StructuredItem>();

  emitAdd(chip: Chip): void {
    this.add.emit(chip.factory());
  }

  /** `+` düğmesine basmak cdkDrag'i başlatmamalı (aksi halde tıklama kaybolur). */
  onAddPointerDown(event: Event): void {
    event.stopPropagation();
  }
```

`structured-palette.component.html` — `palette__chips` içindeki iki `@for` bloğunu şunlarla değiştir:

```html
    @for (c of visibleControlChips; track c.type) {
      <div
        class="palette__chip palette__chip--control"
        [attr.data-testid]="'palette-chip-' + (c.label | translate)"
        cdkDrag
        [cdkDragData]="{ factory: c.factory }"
        (dblclick)="emitAdd(c)"
      >
        {{ c.label | translate }}
        <button
          type="button"
          class="palette__add"
          [attr.data-testid]="'palette-add-' + c.type"
          [title]="'structured.addChip' | translate"
          (mousedown)="onAddPointerDown($event)"
          (click)="emitAdd(c)"
        >+</button>
      </div>
    }
    @for (c of visibleActivityChips; track c.label) {
      <div
        class="palette__chip"
        [attr.data-testid]="'palette-chip-' + c.label"
        cdkDrag
        [cdkDragData]="{ factory: c.factory }"
        (dblclick)="emitAdd(c)"
      >
        {{ c.label }}
        <button
          type="button"
          class="palette__add"
          [attr.data-testid]="'palette-add-' + c.label"
          [title]="'structured.addChip' | translate"
          (mousedown)="onAddPointerDown($event)"
          (click)="emitAdd(c)"
        >+</button>
      </div>
    }
```

`structured-palette.component.scss` — `.palette__chip` bloğunu şununla değiştir (konumlandırma + `+` düğmesi stili):

```scss
.palette__chip {
  position: relative;
  padding: 4px 18px 4px 8px; border: 1px solid #cbd5e1; border-radius: 6px; background: #fff;
  cursor: grab; font-size: 12px;

  &--control { font-weight: 600; border-color: #b6c1d2; background: #f8fafc; }

  &:hover .palette__add, .palette__add:focus-visible { opacity: 1; }
}
.palette__add {
  position: absolute; top: -6px; right: -6px;
  width: 16px; height: 16px; padding: 0; line-height: 14px;
  border: 1px solid #cbd5e1; border-radius: 50%; background: #fff;
  color: #334155; font-size: 12px; cursor: pointer;
  opacity: 0; transition: opacity .12s;
}
```

Ve dosyanın sonundaki `@media (prefers-color-scheme: dark)` bloğunun içine ekle:

```scss
  .palette__add { background: #151b25; border-color: #2a3444; color: #e7ecf3; }
```

`src/RPA.Studio/public/assets/i18n/tr.json` — `structured` nesnesinin içine ekle:

```json
    "addChip": "Akışa ekle (çift tık da eklenir)",
```

`src/RPA.Studio/public/assets/i18n/en.json` — `structured` nesnesinin içine ekle:

```json
    "addChip": "Add to flow (double-click also adds)",
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx ng test --watch=false --browsers=ChromeHeadless --include='**/structured-palette.component.spec.ts'`
Expected: PASS — yeni 3 test dahil, dosyadaki tüm testler yeşil.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.ts \
        src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.html \
        src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.scss \
        src/RPA.Studio/src/app/studio/designer/structured/view/structured-palette.component.spec.ts \
        src/RPA.Studio/public/assets/i18n/tr.json src/RPA.Studio/public/assets/i18n/en.json
git commit -m "feat(studio): yapisal palet cipine cift tik / + ile ekleme sinyali

Palet yalnız 'hangi node' bilgisini yayinlar (add output); yerlestirme
mantigi StructuredViewComponent'e aittir. Suruke-birak yolu korunur.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `addFromPalette` — kural C ile yerleştirme

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.html`
- Test: `src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.spec.ts`

**Interfaces:**
- Consumes: Task 1'in `StructuredPaletteComponent.add: EventEmitter<StructuredItem>` output'u.
- Consumes (mevcut): `findPath(tree, target): Path | null` — `Path = { steps: PathStep[]; index: number }`; `insertItem(tree, seqSteps, index, item): StructuredSequence`; `this.commit(next)`; `this.onSelect(item)`; `this.selected(): StructuredItem | null`.
- Produces: `StructuredViewComponent.addFromPalette(item: StructuredItem): void`.

- [ ] **Step 1: Write the failing test**

`structured-view.component.spec.ts` içine, mevcut `describe('StructuredViewComponent', ...)` bloğunun sonuna ekle:

```ts
  function seededView() {
    const wf = treeToWorkflow([
      step(n('a')),
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] }),
    ], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    return f;
  }

  it('appends to the root when nothing is selected', () => {
    const f = seededView();
    const cmp = f.componentInstance;
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t.length).toBe(3);
    expect((t[2] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('inserts right after the selected step', () => {
    const f = seededView();
    const cmp = f.componentInstance;
    cmp.onSelect(cmp.tree()[0]);
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t.length).toBe(3);
    expect((t[1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('inserts AFTER a selected container, not inside it (rule C)', () => {
    const f = seededView();
    const cmp = f.componentInstance;
    const cont = cmp.tree()[1] as { kind: string; lanes: Record<string, unknown[]> };
    expect(cont.kind).toBe('container');
    cmp.onSelect(cmp.tree()[1]);
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t.length).toBe(3);
    expect((t[2] as { node: { activity: string } }).node.activity).toBe('Web.Click');
    // gövde dokunulmadan kalır
    expect((cmp.tree()[1] as { lanes: Record<string, unknown[]> }).lanes['body'].length).toBe(1);
  });

  it('stays inside the lane when a step inside a container is selected', () => {
    const f = seededView();
    const cmp = f.componentInstance;
    const body = (cmp.tree()[1] as { lanes: Record<string, unknown[]> }).lanes['body'];
    cmp.onSelect(body[0] as never);
    cmp.addFromPalette(newStep('Web.Click'));

    const nextBody = (cmp.tree()[1] as { lanes: Record<string, unknown[]> }).lanes['body'];
    expect(nextBody.length).toBe(2);
    expect((nextBody[1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
    expect(cmp.tree().length).toBe(2);
  });

  it('undoes a palette add', () => {
    const f = seededView();
    const cmp = f.componentInstance;
    cmp.addFromPalette(newStep('Web.Click'));
    expect(cmp.tree().length).toBe(3);
    cmp.undo();
    expect(cmp.tree().length).toBe(2);
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx ng test --watch=false --browsers=ChromeHeadless --include='**/structured-view.component.spec.ts'`
Expected: FAIL — `cmp.addFromPalette is not a function`.

- [ ] **Step 3: Write minimal implementation**

`structured-view.component.ts` — mevcut `addToRoot` metodunun hemen ardına ekle:

```ts
  /**
   * Paletten tıklayarak ekleme (kural C — "seçilinin ardına"): seçim bir imleç konumu gibi
   * davranır ve asla kendiliğinden bir konteynerin içine atlamaz. Konteyner seçiliyken yeni
   * öğe onun İÇİNE değil ARDINA girer; içine ekleme lane'deki `+` menüsü ya da sürükleme ile
   * yapılır. Yeni öğe seçili gelir → art arda tıklayarak lineer akış kurulabilir.
   */
  addFromPalette(item: StructuredItem): void {
    const t = this.tree();
    const sel = this.selected();
    const p = sel ? findPath(t, sel) : null;
    const next = p
      ? insertItem(t, p.steps, p.index + 1, item)
      : insertItem(t, [], t.length, item);
    this.commit(next);
    const added = this.itemAtIndex(next, p ? p.steps : [], p ? p.index + 1 : t.length);
    if (added) { this.onSelect(added); }
  }

  /** `commit` sonrası taze ağaçtan eklenen öğeyi çeker (seçim referans eşitliğine dayanır). */
  private itemAtIndex(tree: StructuredSequence, steps: { lane: string; index: number }[], index: number): StructuredItem | null {
    let seq = tree;
    for (const s of steps) {
      const it = seq[s.index];
      if (it.kind !== 'container') { return null; }
      seq = it.lanes[s.lane as LaneName] ?? [];
    }
    return seq[index] ?? null;
  }
```

`structured-view.component.html` — palet satırını output bağlamasıyla değiştir:

```html
        <app-structured-palette (add)="addFromPalette($event)"></app-structured-palette>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx ng test --watch=false --browsers=ChromeHeadless --include='**/structured-view.component.spec.ts'`
Expected: PASS — yeni 5 test dahil, dosyadaki tüm testler yeşil.

- [ ] **Step 5: Run the full Studio suite**

Run: `cd src/RPA.Studio && npx ng test --watch=false --browsers=ChromeHeadless`
Expected: Tüm testler PASS (taban: 543 + bu plandaki 8 yeni test). Sıfır FAILED.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.ts \
        src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.html \
        src/RPA.Studio/src/app/studio/designer/structured/view/structured-view.component.spec.ts
git commit -m "feat(studio): paletten tikla-ekle secilinin ardina yerlestirir (kural C)

Konteyner seciliyken yeni ogeler icine degil ardina eklenir; ekleme
commit() uzerinden gittigi icin undo/redo ve graphChanged calisir.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Spec kapsam kontrolü

| Spec bölümü | Karşılayan görev |
|---|---|
| Çift tık tetikleyicisi | Task 1 (`(dblclick)="emitAdd(c)"`) |
| Sağ üst `+` düğmesi | Task 1 (`.palette__add`, `position: absolute; top/right: -6px`) |
| `+` sürüklemeyi başlatmaz | Task 1 (`onAddPointerDown` → `stopPropagation`) |
| Sürükle-bırak korunur | Task 1 (`cdkDrag`/`cdkDragData` dokunulmadı; mevcut drop testi regresyon guard'ıdır) |
| Palette yerleştirme bilmez | Task 1 (`add` output'u yalnız `StructuredItem` taşır) |
| Kural C yerleştirme tablosu | Task 2 (4 yerleştirme testi) |
| commit → undo/redo + graphChanged | Task 2 (`this.commit(next)` + undo testi) |
| Yeni öğe seçili gelir | Task 2 (`this.onSelect(added)`) |
| i18n `structured.addChip` TR+EN | Task 1 |
