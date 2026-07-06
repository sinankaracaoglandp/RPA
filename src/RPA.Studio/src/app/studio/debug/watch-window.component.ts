import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslatePipe } from '../../core/translate.pipe';
import { DebugVariable, VariableGroup } from '../../shared/models/debug.model';

/**
 * Variable watch window: shows workflow arguments and execution variables in a
 * scope-grouped tree. Refreshes on breakpoint-hit and step events (parent-driven).
 */
@Component({
  selector: 'app-watch-window',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './watch-window.component.html',
  styleUrls: ['./watch-window.component.scss'],
})
export class WatchWindowComponent {
  readonly groups = input<VariableGroup[]>([]);

  readonly isEmpty = computed(() => this.groups().every((g) => g.variables.length === 0));

  scopeLabelKey(scope: string): string {
    return `debug.watch.scope.${scope}`;
  }

  /** Renders a value for display; objects are JSON-serialised, nullish shown as em dash. */
  formatValue(value: unknown): string {
    if (value === null || value === undefined) {
      return '—';
    }
    if (typeof value === 'object') {
      try {
        return JSON.stringify(value);
      } catch {
        return String(value);
      }
    }
    return String(value);
  }

  trackByScope(_index: number, group: VariableGroup): string {
    return group.scope;
  }

  trackByName(_index: number, variable: DebugVariable): string {
    return variable.name;
  }
}
