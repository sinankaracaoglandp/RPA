import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVariable } from '../../../../shared/models/workflow.model';
import { ExpressionInputComponent } from '../expression-input.component';

export interface WebWaitForSelectorProperties {
  selector: string;
  timeoutMs: number;
  [key: string]: unknown;
}

const DEFAULT_TIMEOUT_MS = 30000;

/** Property editor for the `Web.WaitForSelector` activity (Faz 2 Task 2.6). */
@Component({
  selector: 'app-web-wait-for-selector-property',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe, ExpressionInputComponent],
  templateUrl: './web-wait-for-selector-property.component.html',
  styleUrls: ['./web-wait-for-selector-property.component.scss'],
})
export class WebWaitForSelectorPropertyComponent implements OnChanges {
  @Input() properties: Record<string, unknown> | null | undefined;
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<WebWaitForSelectorProperties>();

  readonly form: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      selector: this.fb.control('', Validators.required),
      timeoutMs: this.fb.control(DEFAULT_TIMEOUT_MS, [Validators.required, Validators.min(1)]),
    });
    this.form.valueChanges.subscribe(() => this.emitIfValid());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['properties']) {
      const props = (this.properties ?? {}) as Partial<WebWaitForSelectorProperties>;
      this.form.patchValue(
        {
          selector: props.selector ?? '',
          timeoutMs: props.timeoutMs ?? DEFAULT_TIMEOUT_MS,
        },
        { emitEvent: false },
      );
    }
  }

  get selectorControl() {
    return this.form.get('selector')!;
  }

  get timeoutMsControl() {
    return this.form.get('timeoutMs')!;
  }

  private emitIfValid(): void {
    if (this.form.valid) {
      const { selector, timeoutMs } = this.form.value;
      this.propertiesChange.emit({ selector, timeoutMs: Number(timeoutMs) });
    }
  }
}
