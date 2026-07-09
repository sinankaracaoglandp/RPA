import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivityCatalogService } from '../../../shared/services/activity-catalog.service';
import { ActivityMetadata, ActivityPort } from '../../../shared/models/activity.model';
import { WorkflowVariable } from '../../../shared/models/workflow.model';
import { SpyElement } from '../../../shared/services/spy.service';
import { SelectorPickerButtonComponent } from './selector-picker-button.component';

interface ExpressionValidationSegment {
  text: string;
  invalid: boolean;
}

/**
 * Metadata güdümlü jenerik özellik editörü. Seçili aktivitenin katalog
 * metadata'sındaki `inputs` listesinden her parametre için bir form alanı üretir.
 * Web.* dışındaki tüm aktivite aileleri (SAP, Excel, Email, File, API ...) için
 * özel editör gerekmeden alan girişi sağlar (Faz 5 properties fallback).
 */
@Component({
  selector: 'app-generic-property',
  standalone: true,
  imports: [CommonModule, FormsModule, SelectorPickerButtonComponent],
  templateUrl: './generic-property.component.html',
  styleUrls: ['./generic-property.component.scss'],
})
export class GenericPropertyComponent {
  private readonly catalog = inject(ActivityCatalogService);
  private readonly cdr = inject(ChangeDetectorRef);

  private _activityType?: string;
  metadata?: ActivityMetadata;
  loading = false;
  error = false;
  editorPort?: ActivityPort;
  editorValue = '';
  variablePickerPort?: ActivityPort;
  editorVariablePickerOpen = false;
  variableError = '';

  @Input()
  set activityType(value: string | undefined) {
    if (value === this._activityType) {
      return;
    }
    this._activityType = value;
    this.loadMetadata(value);
  }
  get activityType(): string | undefined {
    return this._activityType;
  }

  @Input() properties: Record<string, unknown> = {};
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<Record<string, unknown>>();

  get inputs(): ActivityPort[] {
    return this.metadata?.inputs ?? [];
  }

  /** Alan tipini HTML input türüne eşler. */
  inputType(port: ActivityPort): 'text' | 'number' | 'checkbox' | 'password' {
    if ((port.options?.length ?? 0) > 0) {
      return 'text';
    }

    switch ((port.type ?? '').toLowerCase()) {
      case 'int':
      case 'number':
      case 'decimal':
        return 'number';
      case 'bool':
      case 'boolean':
        return 'checkbox';
      case 'credential':
        return 'password';
      default:
        return 'text';
    }
  }

  value(port: ActivityPort): unknown {
    return this.properties[port.name] ?? '';
  }

  boolValue(port: ActivityPort): boolean {
    return this.properties[port.name] === true;
  }

  isVariableField(port: ActivityPort): boolean {
    return ['variableName', 'outputVariable'].includes(port.name);
  }

  isConditionField(port: ActivityPort): boolean {
    return this.activityType === 'Logic.If' && port.name === 'condition';
  }

  showExpressionExamples(port: ActivityPort): boolean {
    return this.isConditionField(port);
  }

  expressionExamples(port: ActivityPort): string[] {
    if (!this.showExpressionExamples(port)) {
      return [];
    }

    return [
      '{{karar}} == 1',
      '{{karar}} != 0',
      '{{adet}} > 5',
      '{{durum}} == "Onaylandi"',
      '{{aktif}} == true',
      '{{tarih}} == "2026-07-09T08:30:00"',
    ];
  }

  hasExpressionSpacingError(port: ActivityPort, value: unknown = this.value(port)): boolean {
    return this.expressionValidationSegments(port, value).some((segment) => segment.invalid);
  }

  expressionValidationMessage(port: ActivityPort, value: unknown = this.value(port)): string {
    if (!this.hasExpressionSpacingError(port, value)) {
      return '';
    }

    return 'Karsilastirma operatorlerinin iki tarafinda bosluk olmali. Ornek: {{karar}} == 1';
  }

  expressionValidationSegments(
    port: ActivityPort,
    value: unknown = this.value(port),
  ): ExpressionValidationSegment[] {
    if (!this.isConditionField(port)) {
      return [{ text: String(value ?? ''), invalid: false }];
    }

    const text = String(value ?? '');
    if (!text) {
      return [];
    }

    const segments: ExpressionValidationSegment[] = [];
    const regex = /(\S)(==|!=|>=|<=|>|<)(\S)/g;
    let lastIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = regex.exec(text)) !== null) {
      const matchIndex = match.index;
      const invalidStart = matchIndex + match[1].length;
      const invalidEnd = invalidStart + match[2].length;

      if (lastIndex < invalidStart) {
        segments.push({ text: text.slice(lastIndex, invalidStart), invalid: false });
      }

      segments.push({ text: text.slice(invalidStart, invalidEnd), invalid: true });
      lastIndex = invalidEnd;
    }

    if (lastIndex < text.length) {
      segments.push({ text: text.slice(lastIndex), invalid: false });
    }

    return segments.length > 0 ? segments : [{ text, invalid: false }];
  }

  onValueChange(port: ActivityPort, raw: unknown): void {
    const next = { ...this.properties };
    if (this.inputType(port) === 'number') {
      const num = raw === '' || raw === null ? null : Number(raw);
      next[port.name] = num;
    } else {
      next[port.name] = raw;
    }
    this.properties = next;
    this.clearVariableError();
    this.propertiesChange.emit(next);
  }

  onPicked(port: ActivityPort, element: SpyElement): void {
    this.onValueChange(port, element.elementId);
  }

  canOpenEditor(port: ActivityPort): boolean {
    return (
      this.inputType(port) !== 'checkbox' &&
      (port.options?.length ?? 0) === 0 &&
      !(this.isVariableField(port) && this.variables.length > 0)
    );
  }

  openEditor(port: ActivityPort): void {
    this.editorPort = port;
    this.editorValue = String(this.value(port) ?? '');
    this.variablePickerPort = undefined;
    this.editorVariablePickerOpen = false;
  }

  closeEditor(): void {
    this.editorPort = undefined;
    this.editorValue = '';
    this.editorVariablePickerOpen = false;
  }

  applyEditor(): void {
    if (!this.editorPort) {
      return;
    }
    this.onValueChange(this.editorPort, this.normalizeEditorValue(this.editorPort, this.editorValue));
    this.closeEditor();
  }

  handleEditorInput(value: string): void {
    if (!this.editorPort) {
      return;
    }
    this.editorValue = this.normalizeEditorValue(this.editorPort, value);
    this.clearVariableError();
  }

  handleEditorKeydown(event: KeyboardEvent): void {
    if (!this.editorPort || !this.isSingleLineEditor(this.editorPort)) {
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      this.applyEditor();
    }
  }

  isSingleLineEditor(port: ActivityPort): boolean {
    const signature = `${port.name} ${port.description ?? ''} ${port.pickerKind ?? ''}`.toLowerCase();
    return (
      signature.includes('selector') ||
      signature.includes('element') ||
      signature.includes('id') ||
      this.inputType(port) === 'number' ||
      this.inputType(port) === 'password'
    );
  }

  openVariablePicker(port: ActivityPort): void {
    if (!this.hasVariables) {
      this.showVariableError();
      return;
    }
    this.variablePickerPort = this.variablePickerPort?.name === port.name ? undefined : port;
    this.editorVariablePickerOpen = false;
    this.clearVariableError();
  }

  openEditorVariablePicker(): void {
    if (!this.hasVariables) {
      this.showVariableError();
      return;
    }
    this.editorVariablePickerOpen = !this.editorVariablePickerOpen;
    this.variablePickerPort = undefined;
    this.clearVariableError();
  }

  selectVariable(port: ActivityPort, variableName: string): void {
    if (!variableName) {
      return;
    }
    const next = `${String(this.value(port) ?? '')}{{${variableName}}}`;
    this.onValueChange(port, next);
    this.variablePickerPort = undefined;
  }

  selectEditorVariable(variableName: string): void {
    if (!this.editorPort || !variableName) {
      return;
    }
    this.editorValue = this.normalizeEditorValue(this.editorPort, `${this.editorValue}{{${variableName}}}`);
    this.editorVariablePickerOpen = false;
    this.clearVariableError();
  }

  get hasVariables(): boolean {
    return (this.variables ?? []).length > 0;
  }

  private loadMetadata(activityType: string | undefined): void {
    this.metadata = undefined;
    this.error = false;
    if (!activityType) {
      return;
    }
    this.loading = true;
    this.catalog.getActivity(activityType).subscribe({
      next: (meta) => {
        this.metadata = meta;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.error = true;
        this.loading = false;
        this.cdr.markForCheck();
      },
    });
  }

  private normalizeEditorValue(port: ActivityPort, value: string): string {
    const next = value ?? '';
    return this.isSingleLineEditor(port) ? next.replace(/[\r\n]+/g, ' ') : next;
  }

  private showVariableError(): void {
    this.variableError = 'Tanimli degisken yok';
    this.variablePickerPort = undefined;
    this.editorVariablePickerOpen = false;
  }

  private clearVariableError(): void {
    this.variableError = '';
  }
}
