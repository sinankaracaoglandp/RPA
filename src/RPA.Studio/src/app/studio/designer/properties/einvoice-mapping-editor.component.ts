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

@Component({
  selector: 'app-einvoice-mapping-editor',
  standalone: true,
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
    if (node.children.length || this.repeatedCount(node) > 1) {
      this.collectionScopeXPath = this.buildXPath(node);
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

  editRule(index: number): void { this.editingIndex = index; this.draft = { ...this.rules[index] }; }
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
