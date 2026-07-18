import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVersion } from '../../../../shared/models/workflow.model';
import { StructuredItem, StructuredSequence } from '../structured-model';
import { workflowToTree } from '../workflow-to-tree';
import { treeToWorkflow } from '../tree-to-workflow';
import { checkStructuralInvariants } from '../structural-invariants';
import { insertItem, removeItem, moveItem, findPath } from '../edit/tree-ops';
import { StructuredSequenceComponent } from './structured-sequence.component';
import { StructuredAddMenuComponent } from './structured-add-menu.component';
import { StructuredAction } from './structured-item.component';

interface ViewState {
  kind: 'empty' | 'tree' | 'fallback';
  tree?: StructuredSequence;
}

@Component({
  selector: 'app-structured-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredSequenceComponent, StructuredAddMenuComponent],
  templateUrl: './structured-view.component.html',
  styleUrls: ['./structured-view.component.scss'],
})
export class StructuredViewComponent {
  // Değişebilir doğruluk kaynağı: workflow'dan bir kez tohumlanır (echo'yu yok say).
  private seeded = false;
  readonly tree = signal<StructuredSequence>([]);
  readonly mode = signal<'empty' | 'tree' | 'fallback'>('empty');

  @Input() set workflow(value: WorkflowVersion | null | undefined) {
    if (this.seeded) { return; }
    this.seeded = true;
    const s = this.convert(value ?? null);
    this.mode.set(s.kind);
    this.tree.set(s.tree ?? []);
  }

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
      next = a.kind === 'delete'
        ? removeItem(t, p)
        : moveItem(t, p, a.kind === 'up' ? -1 : 1);
    }
    this.commit(next);
  }

  addToRoot(item: StructuredItem): void {
    this.commit(insertItem(this.tree(), [], this.tree().length, item));
  }

  private commit(next: StructuredSequence): void {
    this.tree.set(next);
    this.graphChanged.emit(treeToWorkflow(next));
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
    if (!workflow || workflow.nodes.length === 0) {
      return { kind: 'empty' };
    }
    try {
      const tree = workflowToTree(workflow);
      // Güvence: graf yapısal alt-küme değilse (keyfi serbest-graf) fallback.
      if (checkStructuralInvariants(workflow).length > 0) {
        return { kind: 'fallback' };
      }
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
