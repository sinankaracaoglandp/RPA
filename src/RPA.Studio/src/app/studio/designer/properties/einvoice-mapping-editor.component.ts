import { Component, EventEmitter, Input, Output } from '@angular/core';
import { EInvoiceMappingRule, RulePreview, XmlTreeNode, buildXPath, parseSampleXml, previewRule } from './einvoice-mapping.model';

@Component({
  selector: 'app-einvoice-mapping-editor',
  standalone: true,
  templateUrl: './einvoice-mapping-editor.component.html',
  styleUrls: ['./einvoice-mapping-editor.component.scss'],
})
export class EInvoiceMappingEditorComponent {
  private sampleDocument?: XMLDocument;
  tree: XmlTreeNode[] = [];
  rules: EInvoiceMappingRule[] = [];
  sampleError = '';
  draft: EInvoiceMappingRule = {
    name: '', source: 'XPath', valueXPath: '', type: 'string', required: false, multiple: false,
  };

  @Input() set value(value: string | EInvoiceMappingRule[] | null | undefined) {
    if (!value) this.rules = [];
    else this.rules = (typeof value === 'string' ? JSON.parse(value) : value).map((rule: EInvoiceMappingRule) => ({ ...rule }));
  }
  @Output() readonly valueChange = new EventEmitter<string>();

  loadSampleXml(xml: string): void {
    const parsed = parseSampleXml(xml);
    this.sampleDocument = parsed.document;
    this.tree = parsed.tree;
  }

  async onSampleFileSelected(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    try {
      this.loadSampleXml(await file.text());
      this.sampleError = '';
    } catch (error) {
      this.sampleError = error instanceof Error ? error.message : 'XML okunamadı.';
    }
  }

  selectNode(node: XmlTreeNode): void {
    this.draft = { ...this.draft, valueXPath: this.buildXPath(node) };
  }

  updateDraft(field: keyof EInvoiceMappingRule, value: unknown): void {
    this.draft = { ...this.draft, [field]: value };
  }

  addDraftRule(): void {
    if (!this.draft.name.trim() || !this.draft.valueXPath?.trim()) return;
    this.addRule(this.draft);
    this.draft = { ...this.draft, name: '' };
  }

  previewJson(): string {
    return JSON.stringify(this.rules.map(rule => ({ rule: rule.name, ...this.preview(rule) })), null, 2);
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
    return this.sampleDocument ? previewRule(rule, this.sampleDocument) : { raw: null, converted: null, error: 'Örnek XML yüklenmedi.' };
  }
  addRule(rule: EInvoiceMappingRule): void { this.rules = [...this.rules, { ...rule }]; this.emit(); }
  serializedValue(): EInvoiceMappingRule[] { return this.rules.map(rule => ({ ...rule })); }
  private emit(): void { this.valueChange.emit(JSON.stringify(this.serializedValue())); }
}
