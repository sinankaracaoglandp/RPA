import { ChangeDetectorRef, Component, EventEmitter, Input, OnDestroy, Output } from '@angular/core';
import {
  EInvoiceCollectionDefinition,
  EInvoiceMappingRule,
  EInvoiceProfileDefinition,
  RulePreview,
  XmlTreeNode,
  buildXPath,
  ibanPreset,
  kurPreset,
  parseSampleXml,
  previewProfileDefinition,
  previewRule,
  relativizeXPath,
} from './einvoice-mapping.model';
import { RegexWizardComponent } from './regex-wizard.component';

@Component({
  selector: 'app-einvoice-mapping-editor',
  standalone: true,
  imports: [RegexWizardComponent],
  templateUrl: './einvoice-mapping-editor.component.html',
  styleUrls: ['./einvoice-mapping-editor.component.scss'],
})
export class EInvoiceMappingEditorComponent implements OnDestroy {
  constructor(private readonly cdr: ChangeDetectorRef = null!) {}

  private sampleDocument?: XMLDocument;
  tree: XmlTreeNode[] = [];
  rules: EInvoiceMappingRule[] = [];
  collections: EInvoiceCollectionDefinition[] = [];
  collectionName = '';
  collectionScopeXPath = '';
  selectedCollectionName = '';
  sampleError = '';
  private readonly expanded = new Set<XmlTreeNode>();
  editingIndex: number | null = null;
  private regexWorker?: Worker;
  private regexTimer?: ReturnType<typeof setTimeout>;
  private regexSignature = '';
  private regexResult: RulePreview = { raw: null, converted: null };
  regexGroupsValue: Record<string, string> = {};
  draft: EInvoiceMappingRule = {
    name: '', source: 'XPath', valueXPath: '', type: 'string', required: false, multiple: false,
  };

  @Input() set value(value: string | EInvoiceMappingRule[] | null | undefined) {
    if (!value) {
      this.rules = [];
      this.collections = [];
      return;
    }
    const parsed = typeof value === 'string' ? JSON.parse(value) : value;
    if (Array.isArray(parsed)) {
      this.rules = parsed.map((rule: EInvoiceMappingRule) => ({ ...rule }));
      this.collections = [];
      return;
    }
    const definition = parsed as Partial<EInvoiceProfileDefinition>;
    this.rules = (definition.fields ?? []).map((rule: EInvoiceMappingRule) => ({ ...rule }));
    this.collections = (definition.collections ?? []).map((collection: EInvoiceCollectionDefinition) => ({
      name: collection.name,
      scopeXPath: collection.scopeXPath,
      fields: collection.fields.map(field => ({ ...field })),
    }));
    this.cdr?.markForCheck();
  }
  @Output() readonly valueChange = new EventEmitter<string>();
  @Output() readonly profileDefinitionChange = new EventEmitter<string>();

  loadSampleXml(xml: string): void {
    this.sampleDocument = undefined;
    this.tree = [];
    const parsed = parseSampleXml(xml);
    this.sampleDocument = parsed.document;
    this.tree = parsed.tree;
    this.expanded.clear();
    this.allTree().filter(item => item.node.children.length > 0).forEach(item => this.expanded.add(item.node));
    if (this.tree.length) this.activeStep = 2;
    this.cdr?.markForCheck();
  }

  flatTree(): Array<{ node: XmlTreeNode; depth: number }> {
    const result: Array<{ node: XmlTreeNode; depth: number }> = [];
    const visit = (nodes: XmlTreeNode[], depth: number): void => nodes.forEach(node => {
      result.push({ node, depth });
      if (this.expanded.has(node)) visit(node.children, depth + 1);
    });
    visit(this.tree, 0);
    return result;
  }

  private allTree(): Array<{ node: XmlTreeNode; depth: number }> {
    const result: Array<{ node: XmlTreeNode; depth: number }> = [];
    const visit = (nodes: XmlTreeNode[], depth: number): void => nodes.forEach(node => { result.push({ node, depth }); visit(node.children, depth + 1); });
    visit(this.tree, 0);
    return result;
  }

  nodeSample(node: XmlTreeNode): string { return node.children.length ? '' : (node.element.textContent?.trim() ?? ''); }
  repeatedCount(node: XmlTreeNode): number {
    const path = this.buildXPath(node);
    return this.allTree().filter(item => this.buildXPath(item.node) === path).length;
  }
  isExpanded(node: XmlTreeNode): boolean { return this.expanded.has(node); }
  toggleNode(node: XmlTreeNode): void {
    if (this.expanded.has(node)) this.expanded.delete(node); else this.expanded.add(node);
    this.cdr?.markForCheck();
  }

  onTreeKeydown(event: KeyboardEvent, node: XmlTreeNode): void {
    const buttons = Array.from((event.currentTarget as HTMLElement).closest('.einvoice-mapping__tree')!
      .querySelectorAll<HTMLButtonElement>('[data-testid="einvoice-tree-node"]'));
    const index = buttons.indexOf(event.currentTarget as HTMLButtonElement);
    if (event.key === 'ArrowDown' && buttons[index + 1]) buttons[index + 1].focus();
    else if (event.key === 'ArrowUp' && buttons[index - 1]) buttons[index - 1].focus();
    else if (event.key === 'ArrowLeft' && node.children.length && this.expanded.has(node)) {
      this.expanded.delete(node);
    } else if (event.key === 'ArrowLeft' && node.parent) {
      const parentIndex = this.flatTree().findIndex(item => item.node === node.parent);
      buttons[parentIndex]?.focus();
    } else if (event.key === 'ArrowRight' && node.children.length && !this.expanded.has(node)) {
      this.expanded.add(node);
    } else if (event.key === 'ArrowRight' && node.children.length) buttons[index + 1]?.focus();
    else return;
    event.preventDefault();
  }

  async onSampleFileSelected(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    try {
      this.loadSampleXml(await file.text());
      this.sampleError = '';
    } catch (error) {
      this.cdr?.markForCheck();
      this.sampleError = error instanceof Error ? error.message : 'XML okunamadı.';
    }
  }

  selectNode(node: XmlTreeNode): void {
    this.draft = { ...this.draft, valueXPath: this.buildXPath(node) };
    const isBranch = node.children.length > 0 || this.repeatedCount(node) > 1;
    if (isBranch) this.collectionScopeXPath = this.buildXPath(node);

    if (this.repickPath) {
      // Diyalogdaki "Ağaçtan seç" ile gelindi: formu bozmadan yeni yolla geri dön.
      this.repickPath = false;
      this.fieldDialogOpen = true;
      return;
    }
    if (isBranch) return; // dallı/tekrar eden öğe → satır dizisi adayı, form açma
    // Yaprak öğe → doğrudan alan ekleme formunu aç (tıkla-ekle akışı).
    if (!this.fieldDialogOpen) {
      this.editingIndex = null;
      this.editingCollection = null;
      this.editingFieldName = null;
      this.draft = { ...this.draft, name: '', source: 'XPath', type: 'string', required: false, multiple: false };
      this.fieldDialogOpen = true;
    }
  }

  updateDraft(field: keyof EInvoiceMappingRule, value: unknown): void {
    this.draft = { ...this.draft, [field]: value };
    this.cancelRegexPreview();
  }

  applyPreset(kind: 'kur' | 'iban' | 'note'): void {
    this.draft = kind === 'note'
      ? { name: 'note', source: 'InvoiceNotes', valueXPath: '/Invoice/cbc:Note', type: 'string', required: false, multiple: true }
      : { ...(kind === 'kur' ? kurPreset() : ibanPreset()) };
  }

  addDraftRule(): void {
    if (!this.isDraftValid()) return;
    this.addRule(this.draft);
    this.draft = { ...this.draft, name: '' };
  }

  editRule(index: number): void {
    this.editingIndex = index;
    this.editingCollection = null;
    this.editingFieldName = null;
    this.draftTarget = 'root';
    this.draft = { ...this.rules[index] };
    this.fieldDialogOpen = true;
  }
  saveDraftRule(): void {
    if (!this.isDraftValid()) return;
    if (this.editingIndex === null) { this.addDraftRule(); return; }
    this.rules = this.rules.map((rule, index) => index === this.editingIndex ? { ...this.draft } : rule);
    this.editingIndex = null;
    this.emit();
  }
  removeRule(index: number): void { this.rules = this.rules.filter((_, current) => current !== index); this.emit(); }
  private isDraftValid(): boolean {
    const sourceNeedsXPath = this.draft.source === 'XPath';
    return Boolean(this.draft.name.trim() && (!sourceNeedsXPath || this.draft.valueXPath?.trim()));
  }
  regexGroups(): Record<string, string> {
    return this.regexGroupsValue;
  }

  previewJson(): string {
    return JSON.stringify({ mapping: { ...this.draft }, groups: this.regexGroups(), preview: this.preview(this.draft), rules: this.rules.map(mapping => ({ mapping })) }, null, 2);
  }

  addCollection(name: string, scopeXPath: string): void {
    const normalized = name.trim();
    const scope = scopeXPath.trim();
    if (!this.isIdentifier(normalized) || !scope || this.collections.some(item => item.name.toLowerCase() === normalized.toLowerCase())) return;
    this.collections = [...this.collections, { name: normalized, scopeXPath: scope, fields: [] }];
    this.selectedCollectionName = normalized;
    this.emitProfileDefinition();
  }

  addCollectionFromDraft(): void {
    this.addCollection(this.collectionName, this.collectionScopeXPath);
    this.collectionName = '';
    this.collectionScopeXPath = '';
  }

  addCollectionField(collectionName: string, field: EInvoiceMappingRule): void {
    this.collections = this.collections.map(collection => {
      if (collection.name !== collectionName || !this.isIdentifier(field.name)) return collection;
      if (collection.fields.some(item => item.name.toLowerCase() === field.name.toLowerCase())) return collection;
      const valueXPath = field.valueXPath ? relativizeXPath(field.valueXPath, collection.scopeXPath) : field.valueXPath;
      return { ...collection, fields: [...collection.fields, { ...field, valueXPath }] };
    });
    this.emitProfileDefinition();
  }

  addDraftAsCollectionField(): void {
    if (!this.selectedCollectionName || !this.isDraftValid()) return;
    this.addCollectionField(this.selectedCollectionName, this.draft);
  }

  profileDefinition(): EInvoiceProfileDefinition {
    return {
      fields: this.serializedValue(),
      collections: this.collections.map(collection => ({
        name: collection.name,
        scopeXPath: collection.scopeXPath,
        fields: collection.fields.map(field => ({ ...field })),
      })),
    };
  }

  activeStep: 1 | 2 | 3 = 1;
  draftTarget = 'root';
  newCollectionOpen = false;
  fieldDialogOpen = false;
  /** Diyalog "Ağaçtan seç" için geçici kapandı; sonraki ağaç tıklaması formu geri açar. */
  repickPath = false;
  private editingCollection: string | null = null;
  private editingFieldName: string | null = null;

  /** Diyalogdan ağaca dön: form korunur, tıklanan öğenin yolu forma yazılır. */
  pickPathFromTree(): void {
    this.repickPath = true;
    this.fieldDialogOpen = false;
    this.activeStep = 2;
    this.cdr?.markForCheck();
  }

  /** Sağ paneldeki "+ Alan ekle": boş formla diyaloğu açar. */
  openFieldDialog(): void {
    this.editingIndex = null;
    this.editingCollection = null;
    this.editingFieldName = null;
    this.draft = { name: '', source: 'XPath', valueXPath: this.draft.valueXPath ?? '', type: 'string', required: false, multiple: false };
    this.fieldDialogOpen = true;
    this.cdr?.markForCheck();
  }

  closeFieldDialog(): void {
    this.fieldDialogOpen = false;
    this.editingIndex = null;
    this.editingCollection = null;
    this.editingFieldName = null;
    this.wizardTarget = null;
    this.cdr?.markForCheck();
  }

  editCollectionField(collectionName: string, fieldName: string): void {
    const field = this.collections.find(item => item.name === collectionName)?.fields.find(item => item.name === fieldName);
    if (!field) return;
    this.draft = { ...field };
    this.editingCollection = collectionName;
    this.editingFieldName = fieldName;
    this.editingIndex = null;
    this.draftTarget = collectionName;
    this.fieldDialogOpen = true;
    this.cdr?.markForCheck();
  }

  removeCollectionField(collectionName: string, fieldName: string): void {
    this.collections = this.collections.map(collection => collection.name !== collectionName
      ? collection
      : { ...collection, fields: collection.fields.filter(field => field.name !== fieldName) });
    this.emitProfileDefinition();
  }

  /** Diyalogdaki tek kaydet düğmesi: yeni alan / kök alan güncelleme / satır alanı güncelleme. */
  saveField(): void {
    if (!this.isDraftValid()) return;
    if (this.editingCollection && this.editingFieldName) {
      const target = this.editingCollection;
      const original = this.editingFieldName;
      this.collections = this.collections.map(collection => collection.name !== target ? collection : {
        ...collection,
        fields: collection.fields.map(field => field.name !== original ? field : {
          ...this.draft,
          valueXPath: this.draft.valueXPath ? relativizeXPath(this.draft.valueXPath, collection.scopeXPath) : this.draft.valueXPath,
        }),
      });
      this.emitProfileDefinition();
    } else if (this.editingIndex !== null) {
      this.saveDraftRule();
    } else {
      this.addDraft();
    }
    this.closeFieldDialog();
  }

  /** Diyalog başlığı ve kaydet düğmesi metni için. */
  get isEditingField(): boolean {
    return this.editingIndex !== null || this.editingCollection !== null;
  }

  setStep(step: 1 | 2 | 3): void {
    this.activeStep = step;
    this.cdr?.markForCheck();
  }

  /** Alan kartındaki tek "Alanı ekle" butonu: hedefe göre kök kurala veya koleksiyona yazar. */
  addDraft(): void {
    if (this.draftTarget === 'root') {
      this.addDraftRule();
      return;
    }
    this.selectedCollectionName = this.draftTarget;
    this.addDraftAsCollectionField();
    this.draft = { ...this.draft, name: '' };
  }

  wizardTarget: 'regex' | 'fallbackRegex' | null = null;

  openRegexWizard(target: 'regex' | 'fallbackRegex'): void {
    this.wizardTarget = this.wizardTarget === target ? null : target;
  }

  applyWizardPattern(result: { pattern: string; group: string }): void {
    if (this.wizardTarget === 'regex') {
      this.draft = { ...this.draft, regex: result.pattern, group: result.group };
    } else if (this.wizardTarget === 'fallbackRegex') {
      this.draft = { ...this.draft, fallbackRegex: result.pattern, fallbackGroup: result.group };
    }
    this.wizardTarget = null;
    this.cancelRegexPreview();
  }

  /** Sihirbaza verilecek örnek metin: XPath'in bulduğu ham değer; yoksa belgenin notları/metni. */
  wizardSampleText(): string {
    if (!this.sampleDocument) return '';
    if (this.wizardTarget === 'regex') {
      const base = previewRule({ ...this.draft, regex: null, group: null, type: 'string' }, this.sampleDocument);
      if (typeof base.raw === 'string') return base.raw;
      if (Array.isArray(base.raw)) return base.raw.join('\n');
    }
    return this.sampleDocument.documentElement.textContent?.trim() ?? '';
  }

  savedRulePreviews(): Array<{ rule: EInvoiceMappingRule; preview: RulePreview }> {
    if (!this.sampleDocument) {
      return this.rules.map(rule => ({ rule, preview: { raw: null, converted: null, error: 'Örnek XML yüklenmedi.' } }));
    }
    return this.rules.map(rule => ({ rule, preview: previewRule(rule, this.sampleDocument!) }));
  }

  collectionPreviewRows(collection: EInvoiceCollectionDefinition): Array<Record<string, unknown>> {
    if (!this.sampleDocument) return [];
    const preview = previewProfileDefinition({ fields: [], collections: [collection] }, this.sampleDocument);
    const rows = preview[collection.name];
    return Array.isArray(rows) ? rows.slice(0, 5) : [];
  }

  collectionColumns(collection: EInvoiceCollectionDefinition): string[] {
    return collection.fields.map(field => field.name);
  }

  previewText(preview: RulePreview): string {
    if (preview.error) return preview.error;
    if (preview.converted === null || preview.converted === undefined) return '—';
    return Array.isArray(preview.converted) ? preview.converted.map(String).join(', ') : String(preview.converted);
  }

  previewDefinition(): Record<string, any> {
    if (!this.sampleDocument) return {};
    return previewProfileDefinition(this.profileDefinition(), this.sampleDocument);
  }

  emitProfileDefinition(): void {
    this.profileDefinitionChange.emit(JSON.stringify(this.profileDefinition()));
  }

  findFirst(name: string): XmlTreeNode | undefined {
    const visit = (nodes: XmlTreeNode[]): XmlTreeNode | undefined => {
      for (const node of nodes) { if (node.name === name) return node; const found = visit(node.children); if (found) return found; }
      return undefined;
    };
    return visit(this.tree);
  }

  buildXPath(node: XmlTreeNode): string { return buildXPath(node); }
  preview(rule: EInvoiceMappingRule): RulePreview {
    if (!this.sampleDocument) return { raw: null, converted: null, error: 'Örnek XML yüklenmedi.' };
    if (!rule.regex) return previewRule(rule, this.sampleDocument);
    const base = previewRule({ ...rule, regex: null, group: null, type: 'string' }, this.sampleDocument);
    const signature = JSON.stringify([rule.regex, rule.group, rule.type, rule.multiple, base.raw]);
    if (signature !== this.regexSignature) this.startRegexPreview(signature, rule, base.raw);
    return this.regexResult;
  }
  private startRegexPreview(signature: string, rule: EInvoiceMappingRule, raw: RulePreview['raw']): void {
    this.cancelRegexPreview(); this.regexSignature = signature; this.regexResult = { raw: null, converted: null, error: 'Regex önizlemesi bekleniyor.' };
    if (typeof Worker === 'undefined') { this.regexResult = { raw: null, converted: null, error: 'Regex önizlemesi bu ortamda kullanılamıyor.' }; return; }
    this.regexWorker = new Worker(new URL('./einvoice-regex.worker', import.meta.url), { type: 'module' });
    this.regexWorker.onmessage = ({ data }) => {
      this.regexGroupsValue = data.groups ?? {};
      this.regexResult = data.error ? { raw: null, converted: null, error: data.error } : this.convertRegexResult(data.selected, rule);
      this.cancelWorkerOnly(); this.cdr?.markForCheck();
    };
    this.regexTimer = setTimeout(() => { this.regexResult = { raw: null, converted: null, error: 'Regex önizlemesi zaman aşımına uğradı.' }; this.cancelWorkerOnly(); this.cdr?.markForCheck(); }, 75);
    this.regexWorker.postMessage({ pattern: rule.regex, group: rule.group, raw });
  }
  private convertRegexResult(values: string[], rule: EInvoiceMappingRule): RulePreview {
    if (!values.length) return { raw: null, converted: rule.multiple ? [] : null, error: rule.required ? 'Zorunlu değer bulunamadı.' : undefined };
    const doc = new DOMParser().parseFromString(`<v>${values.map(value => `<x>${value.replace(/&/g, '&amp;').replace(/</g, '&lt;')}</x>`).join('')}</v>`, 'application/xml');
    return previewRule({ ...rule, regex: null, group: null, valueXPath: '/v/x' }, doc);
  }
  private cancelWorkerOnly(): void { this.regexWorker?.terminate(); this.regexWorker = undefined; if (this.regexTimer) clearTimeout(this.regexTimer); this.regexTimer = undefined; }
  private cancelRegexPreview(): void { this.cancelWorkerOnly(); this.regexSignature = ''; this.regexGroupsValue = {}; }
  ngOnDestroy(): void { this.cancelRegexPreview(); }
  addRule(rule: EInvoiceMappingRule): void { this.rules = [...this.rules, { ...rule }]; this.emit(); }
  serializedValue(): EInvoiceMappingRule[] { return this.rules.map(rule => ({ ...rule })); }
  private emit(): void {
    this.valueChange.emit(JSON.stringify(this.serializedValue()));
    this.emitProfileDefinition();
  }
  private isIdentifier(value: string): boolean { return /^[A-Za-z_][A-Za-z0-9_]*$/.test(value); }
}
