import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Input,
  Output,
  EventEmitter,
  inject,
  signal,
  computed,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { ComponentDetailService } from '../../../shared/services/component-detail.service';
import { ComponentVersion } from '../../../shared/models/component.model';

/**
 * Multi-step publish wizard dialog for publishing components.
 * Step 1: Component Info (name, version, description)
 * Step 2: JSON Definition
 * Step 3: Test Cases (optional)
 */
@Component({
  selector: 'app-publish-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './publish-wizard.component.html',
  styleUrls: ['./publish-wizard.component.scss'],
})
export class PublishWizardComponent {
  @Input() component?: ComponentVersion;
  @Output() close = new EventEmitter<void>();
  @Output() published = new EventEmitter<ComponentVersion>();

  private readonly service = inject(ComponentDetailService);
  private readonly fb = inject(FormBuilder);

  readonly currentStep = signal(1);
  readonly successMessage = signal(false);
  readonly errorMessage = signal(false);
  readonly isPublishing = signal(false);

  readonly formData = this.fb.group({
    name: ['', [Validators.required]],
    version: ['1.0.0', [Validators.required]],
    description: [''],
    jsonDefinition: ['{}', [Validators.required]],
    testCases: [''],
  });

  isStep1Valid(): boolean {
    const name = this.formData.get('name')?.value?.trim();
    const version = this.formData.get('version')?.value?.trim();
    return !!(name && version);
  }

  isStep2Valid(): boolean {
    const json = this.formData.get('jsonDefinition')?.value?.trim();
    return !!json;
  }

  readonly stepTitle = computed(() => {
    const step = this.currentStep();
    const keys: Record<number, string> = {
      1: 'componentLibrary.publishWizard.step1Title',
      2: 'componentLibrary.publishWizard.step2Title',
      3: 'componentLibrary.publishWizard.step3Title',
    };
    return keys[step] || '';
  });

  readonly stepDescription = computed(() => {
    const step = this.currentStep();
    const keys: Record<number, string> = {
      1: 'componentLibrary.publishWizard.step1Description',
      2: 'componentLibrary.publishWizard.step2Description',
      3: 'componentLibrary.publishWizard.step3Description',
    };
    return keys[step] || '';
  });

  nextStep(): void {
    if (this.currentStep() < 3) {
      this.currentStep.set(this.currentStep() + 1);
    }
  }

  previousStep(): void {
    if (this.currentStep() > 1) {
      this.currentStep.set(this.currentStep() - 1);
    }
  }

  publish(): void {
    if (!this.component || !this.isStep1Valid() || !this.isStep2Valid()) {
      return;
    }

    const version = this.formData.get('version')?.value as string;
    const jsonDefinition = this.formData.get('jsonDefinition')?.value as string;

    if (!version || !jsonDefinition) {
      this.errorMessage.set(true);
      return;
    }

    this.isPublishing.set(true);
    this.successMessage.set(false);
    this.errorMessage.set(false);

    this.service.publishComponent(this.component.componentId, {
      version,
      jsonDefinition,
      inputOutputSchema: '{}',
    }).subscribe({
      next: (result) => {
        this.isPublishing.set(false);
        this.successMessage.set(true);
        this.published.emit(result);
        setTimeout(() => this.close.emit(), 1500);
      },
      error: () => {
        this.isPublishing.set(false);
        this.errorMessage.set(true);
      },
    });
  }

  closeWizard(): void {
    this.close.emit();
  }

  trackByStepNumber(): number {
    return this.currentStep();
  }
}
