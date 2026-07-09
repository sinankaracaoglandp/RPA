import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, EventEmitter, Input, Output, forwardRef, inject } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { WorkflowVariable } from '../../../shared/models/workflow.model';

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
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) inputId!: string;
  @Input({ required: true }) label!: string;
  @Input() placeholder = '';
  @Input() required = false;
  @Input() invalid = false;
  @Input() hint = '';
  @Input() errorMessage = '';
  @Input() variables: WorkflowVariable[] = [];

  @Output() readonly valueChange = new EventEmitter<string>();

  value = '';
  disabled = false;
  editorOpen = false;
  editorValue = '';
  variablePickerOpen = false;
  editorVariablePickerOpen = false;
  variableError = '';

  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: string): void {
    this.value = value ?? '';
    this.cdr.markForCheck();
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
    this.applyValue(value);
    this.clearVariableError();
  }

  handleBlur(): void {
    this.onTouched();
  }

  insertVariableToken(): void {
    if (this.disabled) {
      return;
    }
    if (!this.hasVariables) {
      this.showVariableError();
      return;
    }
    this.variablePickerOpen = !this.variablePickerOpen;
    this.editorVariablePickerOpen = false;
    this.clearVariableError();
  }

  openEditor(): void {
    if (this.disabled) {
      return;
    }
    this.editorValue = this.value;
    this.editorOpen = true;
    this.cdr.markForCheck();
  }

  closeEditor(): void {
    this.editorOpen = false;
    this.editorVariablePickerOpen = false;
    this.cdr.markForCheck();
  }

  applyEditor(): void {
    this.applyValue(this.normalizeEditorValue(this.editorValue));
    this.closeEditor();
  }

  handleEditorInput(value: string): void {
    this.editorValue = this.normalizeEditorValue(value);
    this.clearVariableError();
  }

  handleEditorKeydown(event: KeyboardEvent): void {
    if (!this.isSingleLineEditor) {
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      this.applyEditor();
    }
  }

  get isSingleLineEditor(): boolean {
    const signature = `${this.inputId} ${this.label} ${this.hint}`.toLowerCase();
    return signature.includes('selector');
  }

  get hasVariables(): boolean {
    return (this.variables ?? []).length > 0;
  }

  openEditorVariablePicker(): void {
    if (!this.hasVariables) {
      this.showVariableError();
      return;
    }
    this.editorVariablePickerOpen = !this.editorVariablePickerOpen;
    this.variablePickerOpen = false;
    this.clearVariableError();
  }

  selectVariable(variableName: string): void {
    if (!variableName) {
      return;
    }
    this.applyValue(`${this.value}{{${variableName}}}`);
    this.variablePickerOpen = false;
    this.clearVariableError();
  }

  selectEditorVariable(variableName: string): void {
    if (!variableName) {
      return;
    }
    this.editorValue = this.normalizeEditorValue(`${this.editorValue}{{${variableName}}}`);
    this.editorVariablePickerOpen = false;
    this.clearVariableError();
  }

  private applyValue(value: string): void {
    const next = this.normalizeEditorValue(value);
    this.value = next;
    this.onChange(next);
    this.valueChange.emit(next);
  }

  private normalizeEditorValue(value: string): string {
    const next = value ?? '';
    return this.isSingleLineEditor ? next.replace(/[\r\n]+/g, ' ') : next;
  }

  private showVariableError(): void {
    this.variableError = 'Tanimli degisken yok';
    this.variablePickerOpen = false;
    this.editorVariablePickerOpen = false;
    this.cdr.markForCheck();
  }

  private clearVariableError(): void {
    if (!this.variableError) {
      return;
    }
    this.variableError = '';
    this.cdr.markForCheck();
  }
}
