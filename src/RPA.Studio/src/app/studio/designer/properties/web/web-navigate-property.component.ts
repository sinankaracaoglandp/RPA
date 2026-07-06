import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ExpressionInputComponent } from '../expression-input.component';

export interface WebNavigateProperties {
  url: string;
  waitSelector?: string;
  [key: string]: unknown;
}

/**
 * Property editor for the `Web.Navigate` activity (Faz 2 Task 2.6).
 * Configures the target URL and an optional post-navigation wait selector.
 */
@Component({
  selector: 'app-web-navigate-property',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe, ExpressionInputComponent],
  templateUrl: './web-navigate-property.component.html',
  styleUrls: ['./web-navigate-property.component.scss'],
})
export class WebNavigatePropertyComponent implements OnChanges {
  @Input() properties: Record<string, unknown> | null | undefined;
  @Output() readonly propertiesChange = new EventEmitter<WebNavigateProperties>();

  readonly form: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      url: this.fb.control('', Validators.required),
      waitSelector: this.fb.control(''),
    });
    this.form.valueChanges.subscribe(() => this.emitIfValid());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['properties']) {
      const props = (this.properties ?? {}) as Partial<WebNavigateProperties>;
      this.form.patchValue(
        {
          url: props.url ?? '',
          waitSelector: props.waitSelector ?? '',
        },
        { emitEvent: false },
      );
    }
  }

  get urlControl() {
    return this.form.get('url')!;
  }

  private emitIfValid(): void {
    if (this.form.valid) {
      const { url, waitSelector } = this.form.value;
      this.propertiesChange.emit({
        url,
        ...(waitSelector ? { waitSelector } : {}),
      });
    }
  }
}
