import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '../../core/translate.pipe';

/**
 * Debug step controls: Resume, Step Into, Step Over, Pause, Stop.
 * Emits an event per command; enablement is driven by the parent's state machine.
 */
@Component({
  selector: 'app-step-controls',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './step-controls.component.html',
  styleUrls: ['./step-controls.component.scss'],
})
export class StepControlsComponent {
  /** True while execution is paused at a breakpoint (enables step commands). */
  readonly paused = input(false);
  /** True while execution is running (enables Pause). */
  readonly running = input(false);
  /** True when the Agent is connected and a run is in progress. */
  readonly canControl = input(false);

  readonly resume = output<void>();
  readonly stepInto = output<void>();
  readonly stepOver = output<void>();
  readonly pause = output<void>();
  readonly stop = output<void>();

  onResume(): void {
    this.resume.emit();
  }

  onStepInto(): void {
    this.stepInto.emit();
  }

  onStepOver(): void {
    this.stepOver.emit();
  }

  onPause(): void {
    this.pause.emit();
  }

  onStop(): void {
    this.stop.emit();
  }
}
