import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVariable } from '../../../../shared/models/workflow.model';
import { ExpressionInputComponent } from '../expression-input.component';

export interface WebGetTextProperties {
  selector: string;
  waitSelector?: string;
  outputVariable: string;
  [key: string]: unknown;
}

const VARIABLE_NAME_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/;

/**
 * Property editor for the `Web.GetText` activity (Faz 2 Task 2.6). Unlike
 * the other web activities, `Web.GetText` produces an output (`text`), so
 * the editor also captures the workflow variable name that should receive it.
 */
@Component({
  selector: 'app-web-get-text-property',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe, ExpressionInputComponent],
  templateUrl: './web-get-text-property.component.html',
  styleUrls: ['./web-get-text-property.component.scss'],
})
export class WebGetTextPropertyComponent implements OnChanges {
  @Input() properties: Record<string, unknown> | null | undefined;
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<WebGetTextProperties>();

  readonly form: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      selector: this.fb.control('', Validators.required),
      waitSelector: this.fb.control(''),
      outputVariable: this.fb.control('', [Validators.required, Validators.pattern(VARIABLE_NAME_PATTERN)]),
    });
    this.form.valueChanges.subscribe(() => this.emitIfValid());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['properties']) {
      const props = (this.properties ?? {}) as Partial<WebGetTextProperties>;
      this.form.patchValue(
        {
          selector: props.selector ?? '',
          waitSelector: props.waitSelector ?? '',
          outputVariable: props.outputVariable ?? '',
        },
        { emitEvent: false },
      );
    }
  }

  get selectorControl() {
    return this.form.get('selector')!;
  }

  get outputVariableControl() {
    return this.form.get('outputVariable')!;
  }

  get variableOptions(): WorkflowVariable[] {
    return this.variables ?? [];
  }

  private emitIfValid(): void {
    if (this.form.valid) {
      const { selector, waitSelector, outputVariable } = this.form.value;
      this.propertiesChange.emit({
        selector,
        outputVariable,
        ...(waitSelector ? { waitSelector } : {}),
      });
    }
  }
}
