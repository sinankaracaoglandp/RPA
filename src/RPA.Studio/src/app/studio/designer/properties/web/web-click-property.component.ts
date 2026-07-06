import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ExpressionInputComponent } from '../expression-input.component';

export interface WebClickProperties {
  selector: string;
  waitSelector?: string;
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
  @Output() readonly propertiesChange = new EventEmitter<WebClickProperties>();

  readonly form: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      selector: this.fb.control('', Validators.required),
      waitSelector: this.fb.control(''),
    });
    this.form.valueChanges.subscribe(() => this.emitIfValid());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['properties']) {
      const props = (this.properties ?? {}) as Partial<WebClickProperties>;
      this.form.patchValue(
        {
          selector: props.selector ?? '',
          waitSelector: props.waitSelector ?? '',
        },
        { emitEvent: false },
      );
    }
  }

  get selectorControl() {
    return this.form.get('selector')!;
  }

  private emitIfValid(): void {
    if (this.form.valid) {
      const { selector, waitSelector } = this.form.value;
      this.propertiesChange.emit({
        selector,
        ...(waitSelector ? { waitSelector } : {}),
      });
    }
  }
}
