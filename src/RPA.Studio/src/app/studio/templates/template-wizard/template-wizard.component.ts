import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { TemplateMetadata } from '../../../shared/models/template.model';
import { WorkflowVersion, emptyWorkflow } from '../../../shared/models/workflow.model';

/**
 * 3-step wizard for creating a workflow from a template (Faz 5, Task 5.5).
 * Step 1: confirm template. Step 2: customize (workflow name/description).
 * Step 3: review + create.
 */
@Component({
  selector: 'app-template-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './template-wizard.component.html',
  styleUrls: ['./template-wizard.component.scss'],
})
export class TemplateWizardComponent {
  @Input({ required: true }) template!: TemplateMetadata;
  @Output() readonly close = new EventEmitter<void>();
  @Output() readonly created = new EventEmitter<WorkflowVersion>();

  private readonly fb = inject(FormBuilder);

  readonly currentStep = signal(1);
  readonly errorMessage = signal(false);

  readonly formData = this.fb.group({
    name: ['', [Validators.required]],
    description: [''],
  });

  constructor() {
    // Pre-fill step 2 with the template's own name/description as a starting point.
    queueMicrotask(() => {
      if (this.template) {
        this.formData.patchValue({
          name: this.template.name,
          description: this.template.description ?? '',
        });
      }
    });
  }

  readonly stepTitle = computed(() => {
    const keys: Record<number, string> = {
      1: 'templates.wizard.step1Title',
      2: 'templates.wizard.step2Title',
      3: 'templates.wizard.step3Title',
    };
    return keys[this.currentStep()] || '';
  });

  isStep2Valid(): boolean {
    return !!this.formData.get('name')?.value?.trim();
  }

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

  create(): void {
    if (!this.template || !this.isStep2Valid()) {
      this.errorMessage.set(true);
      return;
    }
    this.errorMessage.set(false);

    let base: WorkflowVersion;
    try {
      base = JSON.parse(this.template.workflowJson) as WorkflowVersion;
    } catch {
      base = emptyWorkflow();
    }

    const name = (this.formData.get('name')?.value ?? '').trim();
    const workflow: WorkflowVersion = {
      ...base,
      id: crypto.randomUUID(),
      name,
    };

    this.created.emit(workflow);
  }

  closeWizard(): void {
    this.close.emit();
  }
}
