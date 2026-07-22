import {
  ChangeDetectionStrategy, Component, EventEmitter, HostListener, Input, Output, inject, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVariable, WorkflowVersion } from '../../../../shared/models/workflow.model';
import { ContainerItem, ContainerType, LaneName, StructuredItem, StructuredSequence, lanesFor } from '../structured-model';
import { treeToWorkflow } from '../tree-to-workflow';
import { CdkDragDrop, CdkDropListGroup } from '@angular/cdk/drag-drop';
import {
  insertItem, removeItem, moveItem, findPath, findSeqPath, reorderInSeq, moveAcross, setItemProps,
  setItemLabel, duplicateItem, removeItems, duplicateItems, moveItemsAcross,
} from '../edit/tree-ops';
import { CONTROL_ACTIVITY_OF } from '../edit/control-activity-map';
import { reduceWorkflow } from '../edit/structured-reducer';
import { enclosingLoopItemVars } from '../edit/loop-item-vars';
import { StructuredSequenceComponent } from './structured-sequence.component';
import { StructuredAddMenuComponent } from './structured-add-menu.component';
import { StructuredPaletteComponent } from './structured-palette.component';
import { StructuredPaletteFilter } from './structured-palette-filter';
import { StructuredAction } from './structured-item.component';

interface ViewState {
  kind: 'empty' | 'tree' | 'fallback';
  tree?: StructuredSequence;
}

export interface StructuredSelection {
  activityType?: string;
  properties: Record<string, unknown>;
  /** Seçili node'u saran ForEach döngülerinin item değişkenleri (autocomplete için). */
  variables: WorkflowVariable[];
}

@Component({
  selector: 'app-structured-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, TranslatePipe, StructuredSequenceComponent, StructuredAddMenuComponent,
    StructuredPaletteComponent, CdkDropListGroup,
  ],
  providers: [StructuredPaletteFilter],
  templateUrl: './structured-view.component.html',
  styleUrls: ['./structured-view.component.scss'],
})
export class StructuredViewComponent {
  // Değişebilir doğruluk kaynağı: workflow'dan bir kez tohumlanır (echo'yu yok say).
  private seeded = false;
  readonly tree = signal<StructuredSequence>([]);
  readonly mode = signal<'empty' | 'tree' | 'fallback'>('empty');
  readonly fallbackReason = signal<string>('');

  @Input() set workflow(value: WorkflowVersion | null | undefined) {
    if (this.seeded) { return; }
    // Taslak HTTP ile geldiği için ilk bağlanma null'dır — bunu tohum saymak ağacı kalıcı olarak
    // boş bırakıyordu (yalnız görünümden çıkıp dönünce, yani bileşen yeniden kurulunca doluyordu).
    // Yalnız gerçek bir workflow geldiğinde tohumla; null "henüz yüklenmedi" demektir.
    if (value == null) {
      this.mode.set('empty');
      return;
    }
    this.seeded = true;
    const s = this.convert(value);
    this.mode.set(s.kind);
    this.tree.set(s.tree ?? []);
  }

  /** Workflow değişkenleri (item türetmede items→şema çözümü için). */
  @Input() variables: WorkflowVariable[] = [];

  @Output() readonly graphChanged = new EventEmitter<WorkflowVersion>();

  get editable(): boolean { return this.mode() === 'tree'; }

  /**
   * Palet kategori filtresi. `StructuredPaletteFilter` bu bileşen seviyesinde sağlandığından
   * dışarıdan (designer → toolbox) erişim bu iki üye üzerinden gider.
   */
  private readonly paletteFilter = inject(StructuredPaletteFilter);
  paletteCategory(): string | null { return this.paletteFilter.category(); }
  setPaletteCategory(category: string | null): void { this.paletteFilter.set(category); }

  // ---- Mutasyon uygulama (findPath + tree-ops) ----
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
      // Çoklu seçimdeki bir öğenin ✕/⧉ düğmesi grubun tamamına uygulanır.
      const group = this.selectedItems();
      if (group.length > 1 && group.includes(a.target)) {
        if (a.kind === 'delete') { this.deleteSelection(); return; }
        if (a.kind === 'duplicate') { this.duplicateSelection(); return; }
      }
      const p = findPath(t, a.target);
      if (!p) { return; }
      if (a.kind === 'rename') {
        next = setItemLabel(t, p, a.label);
      } else if (a.kind === 'duplicate') {
        const r = duplicateItem(t, p);
        if (!r) { return; }
        this.commit(r.tree);
        this.onSelect(r.copy); // kopya seçili gelir → değişecek alan hemen düzenlenebilir
        return;
      } else {
        next = a.kind === 'delete'
          ? removeItem(t, p)
          : moveItem(t, p, a.kind === 'up' ? -1 : 1);
      }
    }
    this.commit(next);
  }

  addToRoot(item: StructuredItem): void {
    this.commit(insertItem(this.tree(), [], this.tree().length, item));
  }

  /**
   * Paletten/toolbox'tan tıklayarak ekleme. Yerleştirme kuralı:
   * - **bir lane seçili → o lane'in sonuna** (kullanıcı `if`in "değilse" panelini tıkladıysa
   *   öğe oraya gider; lane seçimi konteyner seçiminden önceliklidir),
   * - seçim yok → kökün sonuna,
   * - adım seçili → onun hemen ardına (aynı dizide),
   * - **konteyner seçili → İÇİNE, ilk lane'inin sonuna** (while/forEach/for → `body`,
   *   if → `true`, tryCatch → `success`). Seçili bloğu doldurmak en sık istenen davranıştır;
   *   yanlış lane'e düşerse öğe sürüklenerek taşınır.
   * Yeni öğe seçili gelir → art arda tıklayarak akış kurulabilir.
   */
  addFromPalette(item: StructuredItem): void {
    const t = this.tree();
    const lane = this.selectedLane();
    if (lane) {
      const cp = findPath(t, lane.container);
      if (!cp) { return; }
      const steps = [...cp.steps, { lane: lane.lane, index: cp.index }];
      const index = (lane.container.lanes[lane.lane] ?? []).length;
      const next = insertItem(t, steps, index, item);
      this.commit(next);
      const added = this.itemAtIndex(next, steps, index);
      if (added) { this.onSelect(added); }
      return;
    }
    const sel = this.selected();
    const p = sel ? findPath(t, sel) : null;
    let steps = p ? p.steps : [];
    let index = p ? p.index + 1 : t.length;
    if (p && sel && sel.kind === 'container') {
      const lane = lanesFor(sel.type)[0];
      steps = [...p.steps, { lane, index: p.index }];
      index = (sel.lanes[lane] ?? []).length;
    }
    const next = insertItem(t, steps, index, item);
    this.commit(next);
    const added = this.itemAtIndex(next, steps, index);
    if (added) { this.onSelect(added); }
  }

  /** `commit` sonrası taze ağaçtan eklenen öğeyi çeker (seçim referans eşitliğine dayanır). */
  private itemAtIndex(
    tree: StructuredSequence, steps: { lane: string; index: number }[], index: number,
  ): StructuredItem | null {
    let seq = tree;
    for (const s of steps) {
      const it = seq[s.index];
      if (it.kind !== 'container') { return null; }
      seq = it.lanes[s.lane as LaneName] ?? [];
    }
    return seq[index] ?? null;
  }

  // ---- Seçim + özellik düzenleme ----
  readonly selected = signal<StructuredItem | null>(null);
  /**
   * Seçili lane (konteyner paneli). Öğe seçiminden AYRI tutulur: lane bir node değildir,
   * özellik paneli açmaz — yalnız "eklenen node buraya gelsin" hedefidir.
   */
  readonly selectedLane = signal<{ container: ContainerItem; lane: LaneName } | null>(null);
  @Output() readonly nodeSelect = new EventEmitter<StructuredSelection | null>();

  /**
   * Çoklu seçim (Ctrl+tık). `selected` "özellik paneli hangi node'u gösteriyor" demektir ve
   * yalnız TEK öğe seçiliyken doludur; `selectedItems` toplu eylemlerin (taşı/sil/kopyala)
   * hedefidir ve tek seçimde de o tek öğeyi içerir — böylece eylemler tek koda düşer.
   */
  readonly selectedItems = signal<StructuredItem[]>([]);

  onSelect(item: StructuredItem, additive = false): void {
    this.propsEditing = false;
    this.selectedLane.set(null);

    if (additive) {
      const next = this.selectedItems().includes(item)
        ? this.selectedItems().filter((i) => i !== item)
        : [...this.selectedItems(), item];
      this.selectedItems.set(next);
      // Birden çok node seçiliyken hangi node'un özelliği düzenlendiği belirsiz olurdu → panel boş.
      const single = next.length === 1 ? next[0] : null;
      this.selected.set(single);
      this.nodeSelect.emit(single ? this.selectionOf(single) : null);
      return;
    }

    this.selectedItems.set([item]);
    this.selected.set(item);
    this.nodeSelect.emit(this.selectionOf(item));
  }

  isSelected(item: StructuredItem): boolean {
    return this.selectedItems().includes(item);
  }

  /** Seçili grubun tamamını siler (tek undo adımı). */
  deleteSelection(): void {
    const items = this.selectedItems();
    if (items.length === 0) { return; }
    this.commit(removeItems(this.tree(), items));
    this.clearSelection();
  }

  /** Seçili grubun kopyalarını gruptaki son öğenin ardına ekler; kopyalar seçili gelir. */
  duplicateSelection(): void {
    const items = this.selectedItems();
    if (items.length === 0) { return; }
    const r = duplicateItems(this.tree(), items);
    if (r.copies.length === 0) { return; }
    this.commit(r.tree);
    this.selectedItems.set(r.copies);
    this.selected.set(r.copies.length === 1 ? r.copies[0] : null);
    this.nodeSelect.emit(r.copies.length === 1 ? this.selectionOf(r.copies[0]) : null);
  }

  /** Konteyner lane'inin boş alanına tıklama — ekleme hedefini o lane'e sabitler. */
  onSelectLane(target: { container: ContainerItem; lane: LaneName }): void {
    this.propsEditing = false;
    this.selected.set(null);
    this.selectedLane.set(target);
    this.nodeSelect.emit(null);
  }

  clearSelection(): void {
    this.propsEditing = false;
    this.selected.set(null);
    this.selectedItems.set([]);
    this.selectedLane.set(null);
    this.nodeSelect.emit(null);
  }

  private selectionOf(item: StructuredItem): StructuredSelection {
    const variables = enclosingLoopItemVars(this.tree(), item, this.variables);
    if (item.kind === 'step') {
      return {
        activityType: item.node.activity,
        properties: (item.node.properties as Record<string, unknown>) ?? {},
        variables,
      };
    }
    return { activityType: CONTROL_ACTIVITY_OF[item.type as ContainerType], properties: { ...item.props }, variables };
  }

  updateSelectedProps(props: Record<string, unknown>): void {
    const sel = this.selected();
    if (!sel) { return; }
    const p = findPath(this.tree(), sel);
    if (!p) { return; }
    const next = setItemProps(this.tree(), p, props);
    this.commit(next, { props: true });
    let seq = next;
    for (const s of p.steps) {
      const it = seq[s.index];
      if (it.kind !== 'container') { return; }
      seq = it.lanes[s.lane as LaneName] ?? [];
    }
    const fresh = seq[p.index];
    if (fresh) { this.selected.set(fresh); }
  }

  // ---- Sürükle-bırak (CDK → tree-ops) ----
  onDrop(event: CdkDragDrop<StructuredSequence>): void {
    const t = this.tree();
    const toSeq = event.container.data;
    const toSteps = findSeqPath(t, toSeq);
    if (!toSteps) { return; }
    const data = event.item.data as unknown as { factory?: () => StructuredItem };
    let next: StructuredSequence;
    const dragged = event.item.data as unknown as StructuredItem;
    const group = this.selectedItems();
    if (data && typeof data.factory === 'function') {
      next = insertItem(t, toSteps, event.currentIndex, data.factory());
    } else if (group.length > 1 && group.includes(dragged)) {
      // Seçili gruptan bir öğe sürüklendi → grubun tamamı taşınır (belge sırası korunur).
      // CDK'nın `currentIndex`'i AYNI liste içinde "sürüklenen öğe çıkarılmış" listeye göredir,
      // listeler ARASINDA ise çıkarmadan. Grup taşımada indeks aritmetiği bu iki semantiği
      // aynı anda tutturamaz → konumu çapa (bu indeksteki öğe) olarak veriyoruz.
      const destSeq = event.container.data;
      const base = event.previousContainer === event.container
        ? destSeq.filter((x) => x !== dragged)
        : destSeq;
      next = moveItemsAcross(t, group, toSteps, base[event.currentIndex] ?? null);
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

  // ---- Undo/Redo (geçmiş + prop koalesleme) ----
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
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') { return; }
    if (event.key === 'Delete' && this.selectedItems().length > 0) {
      event.preventDefault();
      this.deleteSelection();
      return;
    }
    const key = event.key.toLowerCase();
    if (!(event.ctrlKey || event.metaKey) || (key !== 'z' && key !== 'y')) { return; }
    event.preventDefault();
    const redo = (key === 'z' && event.shiftKey) || key === 'y';
    if (redo) { this.redo(); } else { this.undo(); }
  }

  // ---- Gezinme: zoom + sürükle-pan ----
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

  private panning = false;
  private panX = 0; private panY = 0; private scrollX = 0; private scrollY = 0;
  onPanStart(event: PointerEvent, scroll: HTMLElement): void {
    if (event.button !== 0) { return; }
    // Sürüklenebilir bir kartın (veya düğme/giriş alanının) üzerinden başlayan pointer CDK'nın
    // sürükleme jestidir. CDK pointerdown'ı yalnız İÇ İÇE sürüklemelerde durdurur; kök seviyedeki
    // kartlarda olay buraya kadar kabarır ve pan de başlardı → sürükleme boyunca tuval imlecin
    // altından kayar, hedef lane yerinde durmaz ve konteynerin içine bırakmak imkânsızlaşırdı.
    const target = event.target as HTMLElement | null;
    if (target?.closest?.('.cdk-drag, button, input, select, textarea')) { return; }
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

  private convert(workflow: WorkflowVersion | null): ViewState {
    if (!workflow) {
      return { kind: 'empty' };
    }
    if (workflow.nodes.length === 0) {
      // Boş (ama var olan) workflow → düzenlenebilir boş ağaç: kök "+ ekle" slotu görünür,
      // ilk node yapısal görünümde eklenebilir (aksi halde yalnız salt-okunur mesaj çıkardı).
      return { kind: 'tree', tree: [] };
    }
    const r = reduceWorkflow(workflow);
    if (r.ok) {
      return { kind: 'tree', tree: r.tree };
    }
    this.fallbackReason.set(r.reason);
    return { kind: 'fallback' };
  }
}
