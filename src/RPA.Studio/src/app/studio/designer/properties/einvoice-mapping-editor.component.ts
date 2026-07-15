import { ChangeDetectorRef, Component, EventEmitter, Input, Output } from '@angular/core';
import { EInvoiceMappingRule, RulePreview, XmlTreeNode, buildXPath, ibanPreset, kurPreset, parseSampleXml, previewRule } from './einvoice-mapping.model';

@Component({
  selector: 'app-einvoice-mapping-editor',
  standalone: true,
  templateUrl: './einvoice-mapping-editor.component.html',
  styleUrls: ['./einvoice-mapping-editor.component.scss'],
})
export class EInvoiceMappingEditorComponent {
  constructor(private readonly cdr: ChangeDetectorRef = null!) {}

  private sampleDocument?: XMLDocument;
  tree: XmlTreeNode[] = [];
  rules: EInvoiceMappingRule[] = [];
  sampleError = '';
  private readonly expanded = new Set<XmlTreeNode>();
  draft: EInvoiceMappingRule = {
    name: '', source: 'XPath', valueXPath: '', type: 'string', required: false, multiple: false,
  };

  @Input() set value(value: string | EInvoiceMappingRule[] | null | undefined) {
    if (!value) this.rules = [];
    else this.rules = (typeof value === 'string' ? JSON.parse(value) : value).map((rule: EInvoiceMappingRule) => ({ ...rule }));
  }
  @Output() readonly valueChange = new EventEmitter<string>();

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
  repeatedCount(node: XmlTreeNode): number { return this.allTree().filter(item => item.node.name === node.name).length; }
  isExpanded(node: XmlTreeNode): boolean { return this.expanded.has(node); }

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
  }

  updateDraft(field: keyof EInvoiceMappingRule, value: unknown): void {
    this.draft = { ...this.draft, [field]: value };
  }

  applyPreset(kind: 'kur' | 'iban'): void { this.draft = { ...(kind === 'kur' ? kurPreset() : ibanPreset()) }; }

  addDraftRule(): void {
    if (!this.draft.name.trim() || !this.draft.valueXPath?.trim()) return;
    this.addRule(this.draft);
    this.draft = { ...this.draft, name: '' };
  }

  previewJson(): string {
    return JSON.stringify({ draft: this.preview(this.draft), rules: this.rules.map(rule => ({ rule: rule.name, ...this.preview(rule) })) }, null, 2);
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
