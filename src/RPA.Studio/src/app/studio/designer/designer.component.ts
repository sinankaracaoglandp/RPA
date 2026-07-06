import { CommonModule } from '@angular/common';
import { Component, ViewChild, computed, inject, signal } from '@angular/core';
import { WorkflowVersion } from '../../shared/models/workflow.model';
import { DebugService } from '../../shared/services/debug.service';
import { TranslatePipe } from '../../core/translate.pipe';
import { CanvasComponent } from './canvas/canvas.component';
import { ToolboxComponent } from './toolbox/toolbox.component';
import { DebugPanelComponent } from '../debug/debug-panel.component';

/**
 * Root layout of the workflow designer. Owns the canvas and mediates between it
 * and the surrounding panels (toolbox / debugger).
 */
@Component({
  selector: 'app-designer',
  standalone: true,
  imports: [CommonModule, TranslatePipe, CanvasComponent, ToolboxComponent, DebugPanelComponent],
  templateUrl: './designer.component.html',
})
export class DesignerComponent {
  @ViewChild(CanvasComponent) canvas?: CanvasComponent;

  private readonly debug = inject(DebugService);

  readonly workflow = signal<WorkflowVersion | undefined>(undefined);
  readonly selectedNodeId = signal<string | null>(null);
  readonly currentGraph = signal<WorkflowVersion | undefined>(undefined);
  readonly debugMode = signal(false);

  /** Node ids carrying a breakpoint, for canvas highlighting. */
  readonly breakpointNodeIds = computed(() => this.debug.breakpoints().map((b) => b.nodeId));
  readonly debugCurrentNodeId = this.debug.currentNodeId;

  /** Toggles the debug panel, connecting to RobotHub on first open. */
  async toggleDebug(): Promise<void> {
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
    // In debug mode, clicking a node toggles its breakpoint.
    if (this.debugMode() && nodeId) {
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
