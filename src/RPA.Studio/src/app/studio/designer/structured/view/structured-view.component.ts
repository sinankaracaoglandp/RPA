import { ChangeDetectionStrategy, Component, EventEmitter, HostListener, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVariable, WorkflowVersion } from '../../../../shared/models/workflow.model';
import { ContainerType, LaneName, StructuredItem, StructuredSequence } from '../structured-model';
import { treeToWorkflow } from '../tree-to-workflow';
import { CdkDragDrop, CdkDropListGroup } from '@angular/cdk/drag-drop';
import {
  insertItem, removeItem, moveItem, findPath, findSeqPath, reorderInSeq, moveAcross, setItemProps,
  setItemLabel, duplicateItem,
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
   * Paletten tıklayarak ekleme (kural C — "seçilinin ardına"): seçim bir imleç konumu gibi
   * davranır ve asla kendiliğinden bir konteynerin içine atlamaz. Konteyner seçiliyken yeni
   * öğe onun İÇİNE değil ARDINA girer; içine ekleme lane'deki `+` menüsü ya da sürükleme ile
   * yapılır. Yeni öğe seçili gelir → art arda tıklayarak lineer akış kurulabilir.
   */
  addFromPalette(item: StructuredItem): void {
    const t = this.tree();
    const sel = this.selected();
    const p = sel ? findPath(t, sel) : null;
    const steps = p ? p.steps : [];
    const index = p ? p.index + 1 : t.length;
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
  @Output() readonly nodeSelect = new EventEmitter<StructuredSelection | null>();

  onSelect(item: StructuredItem): void {
    this.propsEditing = false;
    this.selected.set(item);
    this.nodeSelect.emit(this.selectionOf(item));
  }

  clearSelection(): void {
    this.propsEditing = false;
    this.selected.set(null);
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
