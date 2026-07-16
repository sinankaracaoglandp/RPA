import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivityCatalogService } from '../../../shared/services/activity-catalog.service';
import { ActivityMetadata, ActivityPort } from '../../../shared/models/activity.model';
import { WorkflowVariable } from '../../../shared/models/workflow.model';
import { SpyElement } from '../../../shared/services/spy.service';
import { SelectorPickerButtonComponent } from './selector-picker-button.component';
import { VisionSequenceEditorComponent } from './vision-sequence-editor.component';
import { TextOffsetEditorComponent } from './text-offset-editor.component';
import { EInvoiceMappingEditorComponent } from './einvoice-mapping-editor.component';
import { EInvoiceProfile, EInvoiceProfileVersion } from '../../../shared/models/einvoice-profile.model';
import { EInvoiceProfileService } from '../../../shared/services/einvoice-profile.service';

interface ExpressionValidationSegment {
  text: string;
  invalid: boolean;
}

/** Alan giriş kipi: sabit değer, tek değişken referansı veya serbest ifade ({{...}} içeren). */
export type FieldMode = 'value' | 'variable' | 'expression';

/**
 * Metadata güdümlü jenerik özellik editörü. Seçili aktivitenin katalog
 * metadata'sındaki `inputs` listesinden her parametre için bir form alanı üretir.
 * Web.* dışındaki tüm aktivite aileleri (SAP, Excel, Email, File, API ...) için
 * özel editör gerekmeden alan girişi sağlar (Faz 5 properties fallback).
 */
@Component({
  selector: 'app-generic-property',
  standalone: true,
  imports: [CommonModule, FormsModule, SelectorPickerButtonComponent, VisionSequenceEditorComponent, TextOffsetEditorComponent, EInvoiceMappingEditorComponent],
  templateUrl: './generic-property.component.html',
  styleUrls: ['./generic-property.component.scss'],
})
export class GenericPropertyComponent {
  private readonly catalog = inject(ActivityCatalogService);
  private readonly einvoiceProfiles = inject(EInvoiceProfileService);
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
  einvoiceProfileOptions: EInvoiceProfile[] = [];
  einvoiceProfileVersions: EInvoiceProfileVersion[] = [];
  einvoiceProfileError = '';

  /** Alan başına giriş kipi: Değer (literal) / Değişken / İfade. Kullanıcı elle değiştirdiğinde saklanır. */
  private readonly modeOverrides: Record<string, FieldMode> = {};

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
  inputType(port: ActivityPort): 'text' | 'number' | 'checkbox' | 'password' | 'date' | 'datetime-local' {
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
      case 'date':
        return 'date';
      case 'datetime':
        return 'datetime-local';
      default:
        return 'text';
    }
  }

  // ---- Giriş kipi (Değer / Değişken / İfade) ----

  /** Bu alan için kip seçici gösterilsin mi? Yalnız düz metin/sayı/tarih alanlarında. */
  showModeSelector(port: ActivityPort): boolean {
    if ((port.options?.length ?? 0) > 0) return false; // enum → dropdown
    if (this.isVariableField(port) && this.variables.length > 0) return false; // değişken adı alanı
    const t = this.inputType(port);
    return t === 'text' || t === 'number' || t === 'date' || t === 'datetime-local';
  }

  /** Alanın geçerli kipi: kullanıcı seçtiyse o; yoksa mevcut değerden çıkarım. */
  fieldMode(port: ActivityPort): FieldMode {
    return this.modeOverrides[port.name] ?? this.inferMode(String(this.value(port) ?? ''));
  }

  private inferMode(value: string): FieldMode {
    if (/^\{\{[^{}]+\}\}$/.test(value.trim())) return 'variable';
    if (value.includes('{{')) return 'expression';
    return 'value';
  }

  setFieldMode(port: ActivityPort, mode: FieldMode): void {
    this.modeOverrides[port.name] = mode;
    if (mode === 'value' && this.inferMode(String(this.value(port) ?? '')) !== 'value') {
      // İfade/değişkenden düz değere geçişte token'ları temizleme; kullanıcı görsün diye dokunmuyoruz.
    }
    this.clearVariableError();
  }

  /** 'Değişken' kipinde seçilen değişken adını `{{ad}}` olarak yazar. */
  selectFieldVariable(port: ActivityPort, variableName: string): void {
    if (!variableName) {
      this.onValueChange(port, '');
      return;
    }
    this.onValueChange(port, `{{${variableName}}}`);
  }

  /** 'Değişken' kipinde <select> için mevcut değerden değişken adını çıkarır. */
  variableNameOf(port: ActivityPort): string {
    const m = /^\{\{([^{}]+)\}\}$/.exec(String(this.value(port) ?? '').trim());
    return m ? m[1] : '';
  }

  /** 'İfade' kipinde açık `{{` token'ının kısmi metni (autocomplete tetikleyici); yoksa null. */
  openTokenPartial(port: ActivityPort): string | null {
    const val = String(this.value(port) ?? '');
    const open = val.lastIndexOf('{{');
    const close = val.lastIndexOf('}}');
    if (open >= 0 && open > close) {
      return val.slice(open + 2);
    }
    return null;
  }

  /** İfade kipinde açık token'a göre filtrelenmiş değişken önerileri. */
  expressionSuggestions(port: ActivityPort): WorkflowVariable[] {
    const partial = this.openTokenPartial(port);
    if (partial === null) return [];
    const q = partial.trim().toLowerCase();
    return (this.variables ?? []).filter((v) => v.name.toLowerCase().includes(q)).slice(0, 8);
  }

  /** Öneriyi seçince açık `{{...` token'ını `{{ad}}` ile tamamlar. */
  applySuggestion(port: ActivityPort, variableName: string): void {
    const val = String(this.value(port) ?? '');
    const open = val.lastIndexOf('{{');
    const base = open >= 0 ? val.slice(0, open) : val;
    this.onValueChange(port, `${base}{{${variableName}}}`);
  }

  value(port: ActivityPort): unknown {
    return this.properties[port.name] ?? '';
  }

  /** Vision.ClickSequence gibi sıralı görüntü adımı editörü gerektiren alan mı? */
  isSequenceField(port: ActivityPort): boolean {
    return port.pickerKind === 'image-sequence';
  }

  isEInvoiceMapping(port: ActivityPort): boolean {
    return port.pickerKind === 'einvoice-mapping';
  }

  isEInvoiceProfile(port: ActivityPort): boolean {
    return port.pickerKind === 'einvoice-profile';
  }

  loadEInvoiceProfiles(): void {
    const projectId = String(this.properties['projectId'] ?? '').trim();
    if (!projectId) {
      this.einvoiceProfileError = 'Profil listesi icin once projectId girin';
      return;
    }
    this.einvoiceProfileError = '';
    this.einvoiceProfiles.list(projectId).subscribe({
      next: profiles => {
        this.einvoiceProfileOptions = profiles;
        this.cdr.markForCheck();
      },
      error: () => {
        this.einvoiceProfileOptions = [];
        this.einvoiceProfileError = 'E-fatura profilleri yuklenemedi';
        this.cdr.markForCheck();
      },
    });
  }

  selectEInvoiceProfile(profileId: string): void {
    if (!profileId) {
      this.onValueChange({ name: 'profileId', type: 'string' }, '');
      return;
    }
    const projectId = String(this.properties['projectId'] ?? '').trim();
    const selected = { ...this.properties, profileId };
    this.properties = selected;
    this.propertiesChange.emit(selected);
    this.einvoiceProfiles.versions(projectId, profileId).subscribe({
      next: versions => {
        this.einvoiceProfileVersions = versions;
        const latest = [...versions].sort((a, b) => b.version - a.version)[0];
        if (!latest) {
          return;
        }
        const withSchema = {
          ...this.properties,
          profileId,
          profileVersion: latest.version,
          outputSchemaJson: latest.outputSchemaJson,
        };
        this.properties = withSchema;
        this.propertiesChange.emit(withSchema);
        this.cdr.markForCheck();
      },
      error: () => {
        this.einvoiceProfileError = 'E-fatura profil surumleri yuklenemedi';
        this.cdr.markForCheck();
      },
    });
  }

  /** Vision.ClickTextOffset gibi çapa+ofset editörü gerektiren alan mı? */
  isTextOffsetField(port: ActivityPort): boolean {
    return port.pickerKind === 'text-offset';
  }

  /** Editöre geçilecek değeri string'e indirger (JSON dizisi). */
  stringValue(port: ActivityPort): string {
    const v = this.value(port);
    return typeof v === 'string' ? v : '';
  }

  /** selector-picker-button'a geçilecek spy türü ('image-sequence'/'text-offset' spy türü değil, editör ipucu → null). */
  spyPickerKind(port: ActivityPort): 'sap' | 'web' | 'desktop' | 'image' | null {
    return port.pickerKind === 'image-sequence' || port.pickerKind === 'text-offset' || port.pickerKind === 'einvoice-profile'
      ? null
      : (port.pickerKind as 'sap' | 'web' | 'desktop' | 'image' | undefined) ?? null;
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

  // Paket F: image picker sonuclarinin onizlemesi (port.name -> base64 PNG).
  onPicked(port: ActivityPort, element: SpyElement): void {
    if (element.kind === 'image') {
      this.onValueChange(port, element.imageBase64 ?? '');
      return;
    }
    this.onValueChange(port, element.elementId);
  }

  /// <summary>
  // Önizleme her node'un KENDİ kayıtlı değerinden türetilir (port adına göre paylaşılan bir
  // harita değil) — böylece aynı tipteki (aynı port adı) node'lar birbirinin görüntüsünü göstermez
  // ve workflow yeniden açıldığında da önizleme gelir. Değişken/ifade ise önizleme gösterilmez.
  imagePreviewOf(port: ActivityPort): string | null {
    const v = this.value(port);
    if (typeof v !== 'string' || v.length === 0 || v.includes('{{')) {
      return null;
    }
    return v;
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
