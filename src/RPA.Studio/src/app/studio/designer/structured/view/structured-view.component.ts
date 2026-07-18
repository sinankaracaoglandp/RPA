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
      // 1) Girişin kendisi yapısal değişmezleri ihlal ediyorsa (ör. eksik loop-back) → fallback.
      if (checkStructuralInvariants(workflow).length > 0) {
        return { kind: 'fallback' };
      }
      // 2) Ağacı geri çevir; node/bağlantı SAYILARI eşleşmiyorsa (ör. düşen dallar, ekstra entry)
      //    graf sadık biçimde temsil edilememiştir → fallback. (id'ler farklı olduğundan yalnız sayı.)
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
