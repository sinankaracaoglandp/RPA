import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '../../core/translate.pipe';
import { Breakpoint } from '../../shared/models/debug.model';

/**
 * Lists the breakpoints set on workflow nodes. Each entry can be enabled/disabled
 * or removed; the current (hit) node is highlighted.
 */
@Component({
  selector: 'app-breakpoint-list',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './breakpoint-list.component.html',
  styleUrls: ['./breakpoint-list.component.scss'],
})
export class BreakpointListComponent {
  readonly breakpoints = input<Breakpoint[]>([]);
  readonly currentNodeId = input<string | null>(null);

  readonly toggleEnabled = output<string>();
  readonly remove = output<string>();

  onToggle(nodeId: string): void {
    this.toggleEnabled.emit(nodeId);
  }

  onRemove(nodeId: string): void {
    this.remove.emit(nodeId);
  }

  trackByNodeId(_index: number, bp: Breakpoint): string {
    return bp.nodeId;
  }
}
