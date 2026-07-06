import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';

/**
 * View-model handed to the node renderer. Kept independent of the Rete
 * runtime so the component is trivially testable and reusable (e.g. toolbox
 * previews).
 */
export interface CanvasNodeView {
  id: string;
  /** Human-readable title (activity display name or node-type label). */
  label: string;
  /** Node type from the workflow schema (activity, if, forEach, ...). */
  nodeType: string;
  /** Optional activity id (e.g. 'Sap.Nco.CallBapi'). */
  activityId?: string;
  icon?: string;
  selected?: boolean;
  /** A breakpoint is set on this node (debug mode). */
  breakpoint?: boolean;
  /** Execution is currently paused on this node (debug mode). */
  current?: boolean;
}

/**
 * Renders a single activity node card on the canvas. Rendered both by the
 * Rete area (dynamically, via the canvas bridge) and directly in tests.
 */
@Component({
  selector: 'app-canvas-node',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './node.component.html',
  styleUrls: ['./node.component.scss'],
})
export class NodeComponent {
  @Input({ required: true }) node!: CanvasNodeView;

  @Output() readonly nodeSelect = new EventEmitter<string>();
  @Output() readonly nodeDelete = new EventEmitter<string>();
  @Output() readonly connectStart = new EventEmitter<string>();
  @Output() readonly connectDrop = new EventEmitter<string>();

  select(): void {
    this.nodeSelect.emit(this.node.id);
  }

  remove(event: Event): void {
    event.stopPropagation();
    this.nodeDelete.emit(this.node.id);
  }

  onOutSocketDown(event: Event): void {
    // Rete'nin node-drag yakalamasını engelle; bağlantı sürüklemesi başlasın.
    event.stopPropagation();
    event.preventDefault();
    this.connectStart.emit(this.node.id);
  }

  onPointerUp(): void {
    this.connectDrop.emit(this.node.id);
  }
}
