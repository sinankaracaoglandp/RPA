import { CommonModule } from '@angular/common';
import { Component, ViewChild, computed, inject, signal } from '@angular/core';
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
  @ViewChild(CanvasComponent) canvas?: CanvasComponent;

  private readonly debug = inject(DebugService);
  private readonly modeService = inject(ModeService);
  private readonly draft = inject(WorkflowDraftService);

  readonly workflow = signal<WorkflowVersion | undefined>(undefined);
  readonly selectedNodeId = signal<string | null>(null);
  readonly currentGraph = signal<WorkflowVersion | undefined>(undefined);
  readonly debugMode = signal(false);

  readonly mode = this.modeService.mode;
  readonly isSimpleMode = computed(() => this.mode() === 'Simple');

  /** Node ids carrying a breakpoint, for canvas highlighting. Empty in Simple mode. */
  readonly breakpointNodeIds = computed(() =>
    this.isSimpleMode() ? [] : this.debug.breakpoints().map((b) => b.nodeId),
  );
  readonly debugCurrentNodeId = this.debug.currentNodeId;

  constructor() {
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
    await this.canvas?.addNode(activityId);
  }

  onNodeSelect(nodeId: string | null): void {
    this.selectedNodeId.set(nodeId);
    // In debug mode (Advanced only), clicking a node toggles its breakpoint.
    if (!this.isSimpleMode() && this.debugMode() && nodeId) {
      this.debug.toggleBreakpoint(nodeId);
    }
  }

  onGraphChanged(graph: WorkflowVersion): void {
    this.currentGraph.set(graph);
  }

  onDebugCurrentNode(_nodeId: string | null): void {
    // Current node is read from the DebugService signal and bound to the canvas;
    // this handler exists so the panel can notify without extra wiring.
  }
}
