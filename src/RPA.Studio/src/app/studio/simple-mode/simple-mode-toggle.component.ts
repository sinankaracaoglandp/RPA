import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslatePipe } from '../../core/translate.pipe';
import { ModeService } from '../../shared/services/mode.service';

/**
 * Header toggle switching the designer between Advanced (full canvas, debug
 * IDE, component library) and Simple (constrained, non-technical) modes.
 */
@Component({
  selector: 'app-simple-mode-toggle',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './simple-mode-toggle.component.html',
  styleUrls: ['./simple-mode-toggle.component.scss'],
})
export class SimpleModeToggleComponent {
  private readonly modeService = inject(ModeService);

  readonly mode = this.modeService.mode;
  readonly isSimple = computed(() => this.mode() === 'Simple');

  selectAdvanced(): void {
    this.modeService.setMode('Advanced');
  }

  selectSimple(): void {
    this.modeService.setMode('Simple');
  }
}
