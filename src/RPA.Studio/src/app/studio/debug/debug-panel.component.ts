import { ChangeDetectionStrategy, Component, effect, inject, input, output } from '@angular/core';
import { TranslatePipe } from '../../core/translate.pipe';
import { DebugService } from '../../shared/services/debug.service';
import { WorkflowVersion } from '../../shared/models/workflow.model';
import { BreakpointListComponent } from './breakpoint-list.component';
import { StepControlsComponent } from './step-controls.component';
import { WatchWindowComponent } from './watch-window.component';

/**
 * Main Debug/Step-Through IDE panel (Faz 5, Task 5.4). Composes the breakpoint
 * list, watch window and step controls, and orchestrates execution through the
 * DebugService. Surfaces the current (hit) node to the canvas for highlighting.
 */
@Component({
  selector: 'app-debug-panel',
  standalone: true,
  imports: [TranslatePipe, BreakpointListComponent, WatchWindowComponent, StepControlsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './debug-panel.component.html',
  styleUrls: ['./debug-panel.component.scss'],
})
export class DebugPanelComponent {
  private readonly debug = inject(DebugService);

  /** Workflow to execute/debug. */
  readonly workflow = input<WorkflowVersion | undefined>(undefined);
  /** Optional execution arguments. */
  readonly arguments = input<Record<string, unknown>>({});

  /** Emits the current (breakpoint-hit) node id so the canvas can highlight it. */
  readonly currentNodeChange = output<string | null>();

  // Reactive state exposed from the DebugService.
  readonly breakpoints = this.debug.breakpoints;
  readonly variableGroups = this.debug.variableGroups;
  readonly executionState = this.debug.executionState;
  readonly connectionStatus = this.debug.connectionStatus;
  readonly currentNodeId = this.debug.currentNodeId;
  readonly error = this.debug.error;
  readonly canExecute = this.debug.canExecute;
  readonly canControl = this.debug.canControl;
  readonly isPaused = this.debug.isPaused;
  readonly isRunning = this.debug.isRunning;

  constructor() {
    // Forward current-node changes to the canvas highlight.
    effect(() => {
      this.currentNodeChange.emit(this.debug.currentNodeId());
    });
  }

  get hasExecutePermission(): boolean {
    return this.debug.hasExecutePermission();
  }

  /** i18n key for the current connection status badge. */
  get connectionStatusKey(): string {
    return `debug.connection.${this.connectionStatus()}`;
  }

  /** i18n key for the current execution state badge. */
  get executionStateKey(): string {
    return `debug.state.${this.executionState().toLowerCase()}`;
  }

  async onExecute(): Promise<void> {
    const wf = this.workflow();
    if (!wf || !this.canExecute()) {
      return;
    }
    await this.debug.execute(wf, this.arguments()).catch(() => undefined);
  }

  onResume(): void {
    void this.debug.resume().catch(() => undefined);
  }

  onStepInto(): void {
    void this.debug.stepInto().catch(() => undefined);
  }

  onStepOver(): void {
    void this.debug.stepOver().catch(() => undefined);
  }

  onPause(): void {
    void this.debug.pause().catch(() => undefined);
  }

  onStop(): void {
    void this.debug.stop().catch(() => undefined);
  }

  onToggleBreakpointEnabled(nodeId: string): void {
    const bp = this.breakpoints().find((b) => b.nodeId === nodeId);
    if (bp) {
      this.debug.setBreakpointEnabled(nodeId, !bp.enabled);
    }
  }

  onRemoveBreakpoint(nodeId: string): void {
    this.debug.clearBreakpoint(nodeId);
  }
}
