import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { WorkflowVersion } from '../../shared/models/workflow.model';
import { DebugService } from '../../shared/services/debug.service';
import { ModeService } from '../../shared/services/mode.service';
import { WorkflowDraftService } from '../../shared/services/workflow-draft.service';
import { TranslatePipe } from '../../core/translate.pipe';
import { CanvasComponent } from './canvas/canvas.component';
import { ToolboxComponent } from './toolbox/toolbox.component';
import { DebugPanelComponent } from '../debug/debug-panel.component';
import { SimpleModeToggleComponent } from '../simple-mode/simple-mode-toggle.component';
import { SimplifiedToolboxComponent } from '../simple-mode/simplified-toolbox.component';
import { PropertiesPanelComponent } from './properties/properties-panel.component';

/**
 * Root layout of the workflow designer. Owns the canvas and mediates between it
 * and the surrounding panels (toolbox / debugger).
 *
 * In Simple mode (Faz 5, Task 5.5) the debug IDE and full toolbox are hidden
 * in favour of the curated SimplifiedToolboxComponent, and breakpoints cannot
 * be set from the canvas.
 */
@Component({
  selector: 'app-designer',
  standalone: true,
  imports: [
    CommonModule,
    TranslatePipe,
    CanvasComponent,
    ToolboxComponent,
    DebugPanelComponent,
    SimpleModeToggleComponent,
    SimplifiedToolboxComponent,
    PropertiesPanelComponent,
  ],
  templateUrl: './designer.component.html',
})
export class DesignerComponent {
  /**
   * Sinyal tabanlı viewChild: zoneless CD'de dekoratör @ViewChild referansının
   * template binding'e ilk change detection turunda işlenmemesi, ilk kullanıcı
   * etkileşiminin (örn. ilk aktivite ekleme) sessizce kaybolmasına yol açıyordu.
   * Sinyal, resolve olduğunda bağlamaları reaktif olarak günceller.
   */
  readonly canvas = viewChild(CanvasComponent);

  private readonly debug = inject(DebugService);
  private readonly modeService = inject(ModeService);
  private readonly draft = inject(WorkflowDraftService);
  private readonly route = inject(ActivatedRoute);

  readonly workflow = signal<WorkflowVersion | undefined>(undefined);
  readonly selectedNodeId = signal<string | null>(null);
  readonly selectedActivityType = signal<string | undefined>(undefined);
  readonly selectedProperties = signal<Record<string, unknown>>({});
  readonly currentGraph = signal<WorkflowVersion | undefined>(undefined);
  readonly debugMode = signal(false);

  readonly workflowId = signal<string | null>(null);
  readonly dirty = signal(false);
  readonly saveState = signal<'idle' | 'saving' | 'error'>('idle');

  readonly mode = this.modeService.mode;
  readonly isSimpleMode = computed(() => this.mode() === 'Simple');

  /** Node ids carrying a breakpoint, for canvas highlighting. Empty in Simple mode. */
  readonly breakpointNodeIds = computed(() =>
    this.isSimpleMode() ? [] : this.debug.breakpoints().map((b) => b.nodeId),
  );
  readonly debugCurrentNodeId = this.debug.currentNodeId;

  constructor() {
    const routedId = this.route.snapshot.paramMap.get('workflowId');
    if (routedId) {
      this.workflowId.set(routedId);
      this.draft.load(routedId).subscribe({
        next: (wf) => this.workflow.set(wf),
        error: () => this.saveState.set('error'),
      });
      return;
    }
    const pending = this.draft.consumePending();
    if (pending) {
      this.workflow.set(pending);
    }
  }

  /** Toggles the debug panel, connecting to RobotHub on first open. Disabled in Simple mode. */
  async toggleDebug(): Promise<void> {
    if (this.isSimpleMode()) {
      return;
    }
    const next = !this.debugMode();
    this.debugMode.set(next);
    if (next) {
      await this.debug.connect().catch(() => undefined);
    }
  }

  /** Invoked by the toolbox to drop an activity onto the canvas. */
  async addActivity(activityId: string): Promise<void> {
    await this.canvas()?.addNode(activityId);
  }

  onNodeSelect(nodeId: string | null): void {
    this.selectedNodeId.set(nodeId);
    const canvas = this.canvas();
    if (nodeId && canvas) {
      this.selectedActivityType.set(canvas.getNodeActivityId(nodeId));
      this.selectedProperties.set(canvas.getNodeProperties(nodeId));
    } else {
      this.selectedActivityType.set(undefined);
      this.selectedProperties.set({});
    }
    // In debug mode (Advanced only), clicking a node toggles its breakpoint.
    if (!this.isSimpleMode() && this.debugMode() && nodeId) {
      this.debug.toggleBreakpoint(nodeId);
    }
  }

  onPropertiesChange(properties: Record<string, unknown>): void {
    const nodeId = this.selectedNodeId();
    if (nodeId) {
      this.canvas()?.updateNodeProperties(nodeId, properties);
      this.selectedProperties.set(properties);
    }
  }

  onGraphChanged(graph: WorkflowVersion): void {
    this.currentGraph.set(graph);
    this.dirty.set(true);
  }

  onDebugCurrentNode(_nodeId: string | null): void {
    // Current node is read from the DebugService signal and bound to the canvas;
    // this handler exists so the panel can notify without extra wiring.
  }

  /** Kaydeder (yalnız yönlendirilmiş bir workflowId varken). Yeni-taslak modunda no-op. */
  async save(): Promise<void> {
    const id = this.workflowId();
    if (!id) {
      return; // yeni-taslak modu: kalıcı hedef yok (Projelerim'den açılır)
    }
    const graph = this.canvas()?.serialize() ?? this.currentGraph();
    if (!graph) {
      return;
    }
    this.saveState.set('saving');
    // Not: firstValueFrom + await burada kasıtlı kullanılmadı — .then devamı bir
    // mikro-görev turuna erteleniyor ve HttpTestingController.flush() hemen
    // ardından senkron assert eden testler bunu göremiyor. subscribe() next/error
    // callback'leri flush() sırasında senkron çalışır.
    return new Promise<void>((resolve) => {
      this.draft.save(id, graph).subscribe({
        next: () => {
          this.dirty.set(false);
          this.saveState.set('idle');
          resolve();
        },
        error: () => {
          this.saveState.set('error');
          resolve();
        },
      });
    });
  }

  onSaveShortcut(event: Event): void {
    event.preventDefault();
    void this.save();
  }
}
