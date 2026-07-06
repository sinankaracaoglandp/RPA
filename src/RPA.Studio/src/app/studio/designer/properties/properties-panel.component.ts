import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';
import { CanvasComponent } from '../canvas/canvas.component';
import { WebPropertyRouterComponent, isWebActivityType } from './web-property-router.component';

/**
 * Properties panel shown alongside the canvas (Faz 5 Task 5.6). Reads the
 * currently selected node's activity type/properties from the CanvasComponent
 * and routes to the matching activity-family editor. Only the Web.* family
 * is wired here so far; other activity families route through their own
 * routers as those tasks land, following the same pattern.
 */
@Component({
  selector: 'app-properties-panel',
  standalone: true,
  imports: [CommonModule, TranslatePipe, WebPropertyRouterComponent],
  templateUrl: './properties-panel.component.html',
})
export class PropertiesPanelComponent {
  @Input() canvas?: CanvasComponent;
  @Input() selectedNodeId: string | null = null;

  get activityType(): string | undefined {
    return this.selectedNodeId ? this.canvas?.getNodeActivityId(this.selectedNodeId) : undefined;
  }

  get properties(): Record<string, unknown> {
    return this.selectedNodeId ? (this.canvas?.getNodeProperties(this.selectedNodeId) ?? {}) : {};
  }

  get isWebActivity(): boolean {
    return isWebActivityType(this.activityType);
  }

  onPropertiesChange(value: Record<string, unknown>): void {
    if (this.selectedNodeId) {
      this.canvas?.updateNodeProperties(this.selectedNodeId, value);
    }
  }
}
