import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVariable } from '../../../../shared/models/workflow.model';
import { ExpressionInputComponent } from '../expression-input.component';

export interface WebClickStep {
  selector: string;
  action?: 'click' | 'hover';
  waitSelector?: string;
  timeoutMs?: number;
}

export interface WebClickProperties {
  selector?: string;
  action?: 'click' | 'hover';
  waitSelector?: string;
  timeoutMs?: number;
  steps?: WebClickStep[];
  [key: string]: unknown;
}

/** Property editor for the `Web.Click` activity (Faz 2 Task 2.6). */
@Component({
  selector: 'app-web-click-property',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe, ExpressionInputComponent],
  templateUrl: './web-click-property.component.html',
  styleUrls: ['./web-click-property.component.scss'],
})
export class WebClickPropertyComponent implements OnChanges {
  @Input() properties: Record<string, unknown> | null | undefined;
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<WebClickProperties>();
  @ViewChild('selectorEditor') private selectorEditor?: ExpressionInputComponent;
  @ViewChild('waitSelectorEditor') private waitSelectorEditor?: ExpressionInputComponent;

  readonly form: FormGroup;
  steps: WebClickStep[] = [];

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      selector: this.fb.control('', Validators.required),
      action: this.fb.control('click', Validators.required),
      waitSelector: this.fb.control(''),
      timeoutMs: this.fb.control(30000, [Validators.required, Validators.min(1)]),
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['properties']) {
      const props = (this.properties ?? {}) as Partial<WebClickProperties>;
      this.steps = this.normalizeSteps(props);
      this.form.patchValue(
        {
          selector: this.steps.length === 0 ? (props.selector ?? '') : '',
          action: props.action ?? this.steps[0]?.action ?? 'click',
          waitSelector: this.steps.length === 0 ? (props.waitSelector ?? '') : '',
          timeoutMs: props.timeoutMs ?? this.steps[0]?.timeoutMs ?? 30000,
        },
        { emitEvent: false },
      );
    }
  }

  get selectorControl() {
    return this.form.get('selector')!;
  }

  addStep(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { selector, action, waitSelector, timeoutMs } = this.form.value;
    this.steps = [
      ...this.steps,
      {
        selector,
        action,
        ...(waitSelector ? { waitSelector } : {}),
        timeoutMs: Number(timeoutMs),
      },
    ];
    this.form.get('selector')?.reset('', { emitEvent: false });
    this.form.get('waitSelector')?.reset('', { emitEvent: false });
    this.selectorEditor?.writeValue('');
    this.waitSelectorEditor?.writeValue('');
    this.emitSteps();
  }

  removeStep(index: number): void {
    this.steps = this.steps.filter((_, stepIndex) => stepIndex !== index);
    this.emitSteps();
  }

  private emitSteps(): void {
    if (this.steps.length === 0) {
      const { action, timeoutMs } = this.form.value;
      this.propertiesChange.emit({
        steps: [],
        selector: '',
        action,
        timeoutMs: Number(timeoutMs),
      });
      return;
    }

    const first = this.steps[0];
    this.propertiesChange.emit({
      steps: this.steps,
      selector: first.selector,
      action: first.action,
      ...(first.waitSelector ? { waitSelector: first.waitSelector } : {}),
      timeoutMs: first.timeoutMs,
    });
  }

  private normalizeSteps(props: Partial<WebClickProperties>): WebClickStep[] {
    if (Array.isArray(props.steps) && props.steps.length > 0) {
      return props.steps.map((step) => ({
        selector: step.selector,
        action: step.action ?? 'click',
        ...(step.waitSelector ? { waitSelector: step.waitSelector } : {}),
        timeoutMs: step.timeoutMs ?? 30000,
      }));
    }

    return [];
  }
}
