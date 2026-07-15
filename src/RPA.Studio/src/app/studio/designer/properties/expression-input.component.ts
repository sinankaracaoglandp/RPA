import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output, forwardRef, inject } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { WorkflowVariable } from '../../../shared/models/workflow.model';
import { ExpressionFunctionInfo, ExpressionFunctionService } from '../../../shared/services/expression-function.service';

export interface AutocompleteItem {
  kind: 'variable' | 'function';
  label: string;
  detail: string;
  insert: string;
  caretOffsetFromEnd: number; // eklenen metnin sonundan imleç kaç karakter geri
}

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
export class ExpressionInputComponent implements ControlValueAccessor, OnInit {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly fnService = inject(ExpressionFunctionService);

  suggestionsOpen = false;
  activeIndex = 0;
  suggestions: AutocompleteItem[] = [];
  private currentPartial = '';

  ngOnInit(): void {
    this.fnService.load().subscribe();
  }

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
    this.updateSuggestions(this.currentPartialWord(value));
  }

  /** İmleç altındaki kısmi kelimeye göre değişken + fonksiyon önerilerini hesaplar. */
  updateSuggestions(partial: string): void {
    const q = (partial ?? '').trim();
    this.currentPartial = q;
    const vars: AutocompleteItem[] = (this.variables ?? [])
      .filter((v) => v.name.toLowerCase().startsWith(q.toLowerCase()))
      .map((v) => ({
        kind: 'variable',
        label: v.name,
        detail: v.type ?? 'değişken',
        insert: `{{${v.name}}}`,
        caretOffsetFromEnd: 0,
      }));
    const fns: AutocompleteItem[] = this.fnService.filter(q).map((f: ExpressionFunctionInfo) => ({
      kind: 'function',
      label: f.name,
      detail: `${f.category} · ${this.signature(f)}`,
      insert: `${f.name}()`,
      caretOffsetFromEnd: 1, // parantez içine konumlan
    }));
    this.suggestions = [...vars, ...fns];
    this.activeIndex = 0;
    this.suggestionsOpen = this.suggestions.length > 0 && q.length > 0;
    this.cdr.markForCheck();
  }

  applySuggestion(item: AutocompleteItem): void {
    // İmleç sonundaki kısmi kelimeyi (currentPartial) öneriyle değiştir; yoksa sona ekle.
    const base =
      this.currentPartial.length > 0 && this.value.endsWith(this.currentPartial)
        ? this.value.slice(0, this.value.length - this.currentPartial.length)
        : this.value;
    this.applyValue(`${base}${item.insert}`);
    this.suggestionsOpen = false;
    this.cdr.markForCheck();
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.suggestionsOpen) {
      return;
    }
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.activeIndex = Math.min(this.activeIndex + 1, this.suggestions.length - 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.activeIndex = Math.max(this.activeIndex - 1, 0);
        break;
      case 'Enter':
      case 'Tab':
        if (this.suggestions[this.activeIndex]) {
          event.preventDefault();
          this.applySuggestion(this.suggestions[this.activeIndex]);
        }
        break;
      case 'Escape':
        this.suggestionsOpen = false;
        break;
    }
    this.cdr.markForCheck();
  }

  private signature(f: ExpressionFunctionInfo): string {
    const ps = f.parameters.map((p) => (p.optional ? `[${p.name}]` : p.name)).join(', ');
    return `${f.name}(${ps})`;
  }

  /** İmleç sonundaki (son) kelime parçasını döndürür — basit v1: son harf öbeği. */
  private currentPartialWord(value: string): string {
    const m = /([A-Za-z_ğüşöçıİĞÜŞÖÇ][A-Za-z0-9_ğüşöçıİĞÜŞÖÇ]*)$/.exec(value ?? '');
    return m ? m[1] : '';
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
