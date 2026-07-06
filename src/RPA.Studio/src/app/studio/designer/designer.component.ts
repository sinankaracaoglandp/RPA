import { CommonModule } from '@angular/common';
import { Component, ViewChild, signal } from '@angular/core';
import { WorkflowVersion } from '../../shared/models/workflow.model';
import { CanvasComponent } from './canvas/canvas.component';
import { ToolboxComponent } from './toolbox/toolbox.component';

/**
 * Root layout of the workflow designer. Owns the canvas and mediates between it
 * and the surrounding panels (toolbox / properties — added in later Faz 5 tasks).
 */
@Component({
  selector: 'app-designer',
  standalone: true,
  imports: [CommonModule, CanvasComponent, ToolboxComponent],
  templateUrl: './designer.component.html',
})
export class DesignerComponent {
  @ViewChild(CanvasComponent) canvas?: CanvasComponent;

  readonly workflow = signal<WorkflowVersion | undefined>(undefined);
  readonly selectedNodeId = signal<string | null>(null);
  readonly currentGraph = signal<WorkflowVersion | undefined>(undefined);

  /** Invoked by the toolbox (later task) to drop an activity onto the canvas. */
  async addActivity(activityId: string): Promise<void> {
    await this.canvas?.addNode(activityId);
  }

  onNodeSelect(nodeId: string | null): void {
    this.selectedNodeId.set(nodeId);
  }

  onGraphChanged(graph: WorkflowVersion): void {
    this.currentGraph.set(graph);
  }
}
