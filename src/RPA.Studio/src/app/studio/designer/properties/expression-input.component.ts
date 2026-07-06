import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, forwardRef } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';

/**
 * Reusable text input supporting workflow expressions (e.g. `{{variableName}}`).
 * Wraps a plain text control as a ControlValueAccessor so it can be bound via
 * `formControlName` like any other reactive-forms input, while exposing a
 * dedicated `data-testid="expression-input"` hook plus an "insert variable"
 * affordance for future variable-picker integration (Faz 5 property editors).
 */
@Component({
  selector: 'app-expression-input',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './expression-input.component.html',
  styleUrls: ['./expression-input.component.scss'],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => ExpressionInputComponent),
      multi: true,
    },
  ],
})
export class ExpressionInputComponent implements ControlValueAccessor {
  @Input({ required: true }) inputId!: string;
  @Input({ required: true }) label!: string;
  @Input() placeholder = '';
  @Input() required = false;
  @Input() invalid = false;
  @Input() hint = '';
  @Input() errorMessage = '';

  @Output() readonly valueChange = new EventEmitter<string>();

  value = '';
  disabled = false;

  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: string): void {
    this.value = value ?? '';
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  handleInput(value: string): void {
    this.value = value;
    this.onChange(value);
    this.valueChange.emit(value);
  }

  handleBlur(): void {
    this.onTouched();
  }

  insertVariableToken(): void {
    if (this.disabled) {
      return;
    }
    const next = `${this.value}{{variable}}`;
    this.handleInput(next);
  }
}
