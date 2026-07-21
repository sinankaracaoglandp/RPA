import { EInvoiceMappingEditorComponent } from './einvoice-mapping-editor.component';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ibanPreset, kurPreset, previewRule } from './einvoice-mapping.model';

const SAMPLE_UBL = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"><cbc:ID>FTR2026</cbc:ID><cbc:PayableAmount>1234.50</cbc:PayableAmount><cbc:Note>IBAN: TR330006100519786457841326</cbc:Note></Invoice>`;
const MAPPING = { name: 'invoiceId', source: 'XPath' as const, valueXPath: '/Invoice/cbc:ID', type: 'string' as const, required: true, multiple: false };
const SCOPED_UBL = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"><cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:Note>first</cbc:Note></cac:InvoiceLine><cac:InvoiceLine><cbc:ID>2</cbc:ID><cbc:Note>second</cbc:Note></cac:InvoiceLine></Invoice>`;
const UBL_WITH_TWO_LINES = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"><cac:InvoiceLine><cac:Item><cbc:ID>M-1</cbc:ID><cbc:Name>Kalem 1</cbc:Name></cac:Item></cac:InvoiceLine><cac:InvoiceLine><cac:Item><cbc:ID>M-2</cbc:ID><cbc:Name>Kalem 2</cbc:Name></cac:Item></cac:InvoiceLine></Invoice>`;
const DEEP_UBL = `<Invoice><Lines><Line><Details><Code>ABC</Code></Details></Line><Line><Details><Code>DEF</Code></Details></Line></Lines></Invoice>`;
const PREVIEW_SAMPLE = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
  xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
  <cbc:ID>FTR2026001</cbc:ID>
  <cbc:Note>IBAN: TR120001200012345678901234</cbc:Note>
  <cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:InvoicedQuantity>2</cbc:InvoicedQuantity></cac:InvoiceLine>
  <cac:InvoiceLine><cbc:ID>2</cbc:ID><cbc:InvoicedQuantity>5</cbc:InvoicedQuantity></cac:InvoiceLine>
</Invoice>`;
let workerHangs = false;
const workers: FakeWorker[] = [];
class FakeWorker {
  onmessage: ((event: MessageEvent) => void) | null = null;
  terminated = false;
  constructor(..._args: unknown[]) { workers.push(this); }
  postMessage(data: { pattern: string; group?: string; raw: string | string[] }): void {
    if (workerHangs) return;
    const values = Array.isArray(data.raw) ? data.raw : [data.raw];
    const selected: string[] = []; let groups: Record<string, string> = {};
    for (const value of values) { const match = new RegExp(data.pattern).exec(value); if (!match) continue; groups = { ...(match.groups ?? {}) }; match.slice(1).forEach((v, i) => { if (v) groups[String(i + 1)] = v; }); selected.push(data.group ? (/^\d+$/.test(data.group) ? match[Number(data.group)] : match.groups?.[data.group])! : match[0]); }
    this.onmessage?.({ data: { selected, groups } } as MessageEvent);
  }
  terminate(): void { this.terminated = true; }
}

describe('EInvoiceMappingEditorComponent', () => {
  let fixture: ComponentFixture<EInvoiceMappingEditorComponent>;

  beforeEach(async () => {
    workerHangs = false; workers.length = 0;
    vi.stubGlobal('Worker', FakeWorker);
    await TestBed.configureTestingModule({ imports: [EInvoiceMappingEditorComponent] }).compileComponents();
    fixture = TestBed.createComponent(EInvoiceMappingEditorComponent);
    fixture.detectChanges();
  });

  it('renders accessible tree rule and preview panels with an XML-only file picker', () => {
    const root = fixture.nativeElement as HTMLElement;
    const picker = root.querySelector('input[type="file"]') as HTMLInputElement;
    expect(picker.accept).toBe('.xml,text/xml,application/xml');

    fixture.componentInstance.setStep(2); fixture.detectChanges();
    expect(root.querySelector('[data-testid="einvoice-tree-panel"]')).toBeTruthy();
    expect(root.querySelector('[data-testid="einvoice-fields-panel"]')).toBeTruthy();

    fixture.componentInstance.setStep(3); fixture.detectChanges();
    expect(root.querySelector('[data-testid="einvoice-preview-panel"]')).toBeTruthy();
  });

  it('reads the sample with File.text without emitting the file or XML', async () => {
    const component = fixture.componentInstance;
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    const file = { text: vi.fn().mockResolvedValue(SAMPLE_UBL) } as unknown as File;

    await component.onSampleFileSelected({ target: { files: [file] } } as unknown as Event);
    fixture.detectChanges();

    expect(file.text).toHaveBeenCalledOnce();
    expect(component.findFirst('cbc:ID')).toBeTruthy();
    expect(emitted).toEqual([]);
  });

  it('renders and selects nodes at unlimited depth with sample and repeated count', () => {
    fixture.componentInstance.loadSampleXml(DEEP_UBL);
    expect(fixture.componentInstance.tree.length).toBe(1);
    expect(fixture.componentInstance.flatTree().some(item => item.node.name === 'Code')).toBe(true);
    fixture.detectChanges();
    expect(fixture.componentInstance.tree.length).toBe(1);
    const code = fixture.nativeElement.querySelector('[data-node-name="Code"]') as HTMLButtonElement;
    expect(code.textContent).toContain('ABC');
    expect(code.textContent).toContain('2');
    code.click();
    expect(fixture.componentInstance.draft.valueXPath).toBe('/Invoice/Lines/Line/Details/Code');
  });

  it('offers every rule field and editable kur and IBAN presets', () => {
    const root = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.setStep(2);
    fixture.componentInstance.openFieldDialog();
    fixture.detectChanges();
    for (const id of ['source', 'scope', 'xpath', 'regex', 'group', 'type'])
      expect(root.querySelector(`#einvoice-rule-${id}`)).toBeTruthy();
    (root.querySelector('[data-testid="einvoice-preset-iban"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.draft).toEqual(ibanPreset());
    expect((root.querySelector('#einvoice-rule-name') as HTMLInputElement).value).toBe('iban');
  });

  it('previews the current draft including raw group and conversion before add', () => {
    fixture.componentInstance.loadSampleXml(SAMPLE_UBL);
    expect(fixture.componentInstance.preview(ibanPreset()).converted).toBe('TR330006100519786457841326');
    fixture.componentInstance.draft = ibanPreset();
    fixture.componentInstance.setStep(3);
    fixture.detectChanges();
    expect(fixture.componentInstance.preview(ibanPreset()).converted).toBe('TR330006100519786457841326');
    const preview = fixture.nativeElement.querySelector('[data-testid="einvoice-draft-preview"]') as HTMLElement;
    expect(preview.textContent).toContain('TR330006100519786457841326');
    expect(preview.textContent).toContain('"source": "InvoiceNotes"');
    expect(preview.textContent).toContain('"iban": "TR330006100519786457841326"');
    expect(fixture.componentInstance.rules).toEqual([]);
  });

  it('provides an editable common invoice-note starter preset', () => {
    fixture.componentInstance.setStep(2);
    fixture.componentInstance.openFieldDialog();
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('[data-testid="einvoice-preset-note"]') as HTMLButtonElement;
    button.click(); fixture.detectChanges();
    expect(fixture.componentInstance.draft.source).toBe('InvoiceNotes');
    expect(fixture.componentInstance.draft.valueXPath).toContain('cbc:Note');
    expect((fixture.nativeElement.querySelector('#einvoice-rule-name') as HTMLInputElement).value).toBe('note');
  });

  it('toggles branch disclosure by pointer and keeps aria-expanded in sync', () => {
    fixture.componentInstance.loadSampleXml(DEEP_UBL); fixture.detectChanges();
    const toggle = fixture.nativeElement.querySelector('[data-toggle-name="Lines"]') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click(); fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(fixture.nativeElement.querySelector('[data-node-name="Code"]')).toBeFalsy();
  });

  it('counts repeated nodes by equivalent path rather than global tag name', () => {
    fixture.componentInstance.loadSampleXml('<Root><A><Code>1</Code><Code>2</Code></A><B><Code>3</Code></B></Root>');
    const codes = fixture.componentInstance.flatTree().filter(item => item.node.name === 'Code');
    expect(fixture.componentInstance.repeatedCount(codes[0].node)).toBe(2);
    expect(fixture.componentInstance.repeatedCount(codes[2].node)).toBe(1);
  });

  it('edits and removes persisted rules after reopening and emits mapping JSON only', () => {
    const component = fixture.componentInstance;
    component.value = JSON.stringify([MAPPING]);
    const emitted: string[] = []; component.valueChange.subscribe(value => emitted.push(value));
    component.editRule(0); component.updateDraft('name', 'updated'); component.saveDraftRule();
    expect(JSON.parse(emitted.at(-1)!)[0].name).toBe('updated');
    component.removeRule(0);
    expect(JSON.parse(emitted.at(-1)!)).toEqual([]);
    expect(emitted.join('')).not.toContain('<Invoice');
  });

  it.each([
    { field: 'name' as const, value: '' },
    { field: 'valueXPath' as const, value: '   ' },
  ])('does not emit an edited rule with blank $field', ({ field, value }) => {
    const component = fixture.componentInstance;
    component.value = JSON.stringify([MAPPING]);
    const emitted: string[] = []; component.valueChange.subscribe(item => emitted.push(item));
    component.editRule(0); component.updateDraft(field, value); component.saveDraftRule();
    expect(emitted).toEqual([]);
    expect(component.rules).toEqual([MAPPING]);
  });

  it('times out and terminates a catastrophic regex worker without main-thread execution', () => {
    vi.useFakeTimers(); workerHangs = true;
    const component = new EInvoiceMappingEditorComponent(); component.loadSampleXml(SAMPLE_UBL);
    expect(component.preview({ ...MAPPING, regex: '(a+)+$', valueXPath: '/Invoice/cbc:Note' }).error).toContain('bekleniyor');
    vi.advanceTimersByTime(76);
    expect(component.preview({ ...MAPPING, regex: '(a+)+$', valueXPath: '/Invoice/cbc:Note' }).error).toContain('zaman aşımına');
    expect(workers[0].terminated).toBe(true);
    vi.useRealTimers();
  });

  it('previews only the draft when saved rules contain distinct regexes', () => {
    workerHangs = true;
    const component = fixture.componentInstance;
    component.loadSampleXml(SAMPLE_UBL);
    component.rules = [
      { ...MAPPING, name: 'saved-a', regex: 'FTR(?<a>\\d+)', group: 'a' },
      { ...MAPPING, name: 'saved-b', regex: 'IBAN: (?<b>TR\\d+)', group: 'b' },
    ];
    component.draft = { ...ibanPreset(), regex: 'IBAN: (?<draft>TR\\d{24})', group: 'draft' };

    const first = JSON.parse(component.previewJson());
    const draftWorker = workers[0];
    const second = JSON.parse(component.previewJson());

    expect(workers).toHaveLength(1);
    expect(workers[0]).toBe(draftWorker);
    expect(first.rules).toEqual(component.rules.map(mapping => ({ mapping })));
    expect(second.preview.error).toContain('bekleniyor');
  });

  it.each(['InvoiceNotes', 'LineNotes'] as const)('accepts %s without valueXPath', source => {
    const component = fixture.componentInstance; const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    component.draft = { name: 'note', source, type: 'string', required: false, multiple: true };
    component.saveDraftRule();
    expect(JSON.parse(emitted[0])[0].source).toBe(source);
  });

  it('clears stale sample state when a replacement XML is malformed', async () => {
    fixture.componentInstance.loadSampleXml(SAMPLE_UBL);
    const file = { text: vi.fn().mockResolvedValue('<Invoice>') } as unknown as File;
    await fixture.componentInstance.onSampleFileSelected({ target: { files: [file] } } as unknown as Event);
    expect(fixture.componentInstance.tree).toEqual([]);
    expect(fixture.componentInstance.preview(fixture.componentInstance.draft).error).toContain('yüklenmedi');
  });

  it('moves tree focus with arrows and collapses or expands branches', () => {
    fixture.componentInstance.loadSampleXml(DEEP_UBL);
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('[data-testid="einvoice-tree-node"]');
    (buttons[0] as HTMLButtonElement).focus();
    (buttons[0] as HTMLButtonElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(buttons[1]);
    (buttons[1] as HTMLButtonElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    expect(document.activeElement).toBe(buttons[1]);
    expect(fixture.componentInstance.isExpanded(fixture.componentInstance.findFirst('Lines')!)).toBe(false);
    (buttons[1] as HTMLButtonElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    expect(document.activeElement).toBe(buttons[0]);
  });
  it('builds namespace-aware xpath and named regex groups', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(SAMPLE_UBL);
    const id = component.findFirst('cbc:ID')!;
    expect(component.buildXPath(id)).toBe('/Invoice/cbc:ID');
    const preview = component.preview({ name: 'year', source: 'XPath', valueXPath: '/Invoice/cbc:ID', regex: '^FTR(?<value>\\d{4})', group: 'value', type: 'string', required: false, multiple: false });
    expect(preview.error).toBeUndefined();
    expect(preview.converted).toBe('2026');
  });

  it('never emits sample xml in mapping value', () => {
    const component = new EInvoiceMappingEditorComponent();
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    component.loadSampleXml(SAMPLE_UBL);
    component.addRule(MAPPING);
    expect(JSON.stringify(component.serializedValue())).not.toContain('<Invoice');
    expect(emitted.every(value => !value.includes('<Invoice'))).toBe(true);
  });

  it('converts typed previews and reports conversion errors', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(SAMPLE_UBL);
    expect(component.preview({ ...MAPPING, valueXPath: '/Invoice/cbc:PayableAmount', type: 'decimal' }).converted).toBe(1234.5);
    expect(component.preview({ ...MAPPING, type: 'integer' }).error).toContain('integer');
  });

  it('provides kur and IBAN presets without sample data', () => {
    expect(kurPreset().type).toBe('decimal');
    expect(ibanPreset().group).toBe('iban');
    const doc = new DOMParser().parseFromString(SAMPLE_UBL, 'application/xml');
    expect(previewRule(ibanPreset(), doc).converted).toBe('TR330006100519786457841326');
  });

  it('rejects malformed XML', () => {
    expect(() => new EInvoiceMappingEditorComponent().loadSampleXml('<Invoice>')).toThrowError('Geçersiz XML örneği.');
  });

  it('evaluates value xpath relative to each invoice-line scope', () => {
    const doc = new DOMParser().parseFromString(SCOPED_UBL, 'application/xml');
    const preview = previewRule({ ...MAPPING, scopeXPath: '/Invoice/cac:InvoiceLine', valueXPath: './/cbc:Note', multiple: true }, doc);
    expect(preview.error).toBeUndefined();
    expect(preview.converted).toEqual(['first', 'second']);
  });

  it('creates a collection from a repeated XML scope and relative child mappings', () => {
    const component = fixture.componentInstance;
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    component.addCollection('satirlar', '/Invoice/cac:InvoiceLine');
    component.addCollectionField('satirlar', {
      name: 'MalzemeKodu',
      source: 'XPath',
      valueXPath: './cac:Item/cbc:ID',
      type: 'string',
      required: true,
      multiple: false,
    });

    expect(component.previewDefinition()['satirlar']).toHaveLength(2);
    expect(component.previewDefinition()['satirlar'][0].MalzemeKodu).toBe('M-1');
    expect(component.profileDefinition().collections[0].fields[0].name).toBe('MalzemeKodu');
  });

  it('loads an existing profile definition with root and collection fields into the editor', () => {
    const component = fixture.componentInstance;
    component.value = JSON.stringify({
      fields: [{ ...MAPPING, name: 'faturaNo' }],
      collections: [
        {
          name: 'satirlar',
          scopeXPath: '/Invoice/cac:InvoiceLine',
          fields: [{ ...MAPPING, name: 'MalzemeKodu', valueXPath: './cac:Item/cbc:ID' }],
        },
      ],
    });
    component.setStep(2);
    fixture.detectChanges();

    expect(component.rules.map(rule => rule.name)).toEqual(['faturaNo']);
    expect(component.collections[0].name).toBe('satirlar');
    expect(component.collections[0].fields[0].name).toBe('MalzemeKodu');
    expect(fixture.nativeElement.textContent).toContain('satirlar.MalzemeKodu');
  });

  it('lets the user add a visible invoice-line collection field from the selected XML node', () => {
    const component = fixture.componentInstance;
    const emitted: string[] = [];
    component.profileDefinitionChange.subscribe(value => emitted.push(value));
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-node-name="cac:InvoiceLine"]') as HTMLButtonElement).click();
    component.newCollectionOpen = true;
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="collection-name"]') as HTMLInputElement).value = 'satirlar';
    (fixture.nativeElement.querySelector('[data-testid="collection-name"]') as HTMLInputElement)
      .dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('[data-testid="add-collection"]') as HTMLButtonElement).click();

    component.draft = {
      name: 'MalzemeKodu',
      source: 'XPath',
      valueXPath: './cac:Item/cbc:ID',
      type: 'string',
      required: true,
      multiple: false,
    };
    component.draftTarget = 'satirlar';
    component.fieldDialogOpen = true;
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="einvoice-add-rule"]') as HTMLButtonElement).click();

    expect(component.collections[0].fields[0].name).toBe('MalzemeKodu');
    expect(JSON.parse(emitted.at(-1)!).collections[0].fields[0].name).toBe('MalzemeKodu');
  });

  it('never emits sample XML while saving a profile draft definition', () => {
    const component = fixture.componentInstance;
    const emitted: string[] = [];
    component.profileDefinitionChange.subscribe(value => emitted.push(value));
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    component.addCollection('satirlar', '/Invoice/cac:InvoiceLine');
    component.addCollectionField('satirlar', { ...MAPPING, name: 'MalzemeKodu', valueXPath: './cac:Item/cbc:ID' });
    component.emitProfileDefinition();

    expect(emitted.length).toBeGreaterThanOrEqual(1);
    expect(JSON.stringify(emitted)).not.toContain('<Invoice');
    expect(JSON.parse(emitted.at(-1)!).collections[0].name).toBe('satirlar');
  });

  it.each(['missing', '9'])('reports missing regex group %s instead of converting an empty value', group => {
    const doc = new DOMParser().parseFromString(SAMPLE_UBL, 'application/xml');
    const preview = previewRule({ ...MAPPING, regex: 'FTR(?<year>\\d{4})', group }, doc);
    expect(preview.converted).toBeNull();
    expect(preview.error).toContain('Regex group');
  });

  it('keeps an exact valid yyyy-MM-dd preview as a date-only string', () => {
    const doc = new DOMParser().parseFromString(SAMPLE_UBL.replace('FTR2026', '2026-07-15'), 'application/xml');
    const preview = previewRule({ ...MAPPING, type: 'date' }, doc);
    expect(preview.error).toBeUndefined();
    expect(preview.converted).toBe('2026-07-15');
  });

  it.each(['2026-02-30', '07/15/2026', '2026-07-15T00:00:00Z'])(
    'rejects non-DateOnly value %s',
    value => {
      const doc = new DOMParser().parseFromString(SAMPLE_UBL.replace('FTR2026', value), 'application/xml');
      const preview = previewRule({ ...MAPPING, type: 'date' }, doc);
      expect(preview.converted).toBeNull();
      expect(preview.error).toContain('date');
    },
  );

  it("koleksiyona eklenen alanın mutlak XPath yolu scope'a göre göreceli kaydedilir", () => {
    const component = new EInvoiceMappingEditorComponent();
    component.addCollection('satirlar', '/Invoice/cac:InvoiceLine');
    component.addCollectionField('satirlar', {
      name: 'MalzemeKodu',
      source: 'XPath',
      valueXPath: '/Invoice/cac:InvoiceLine/cac:Item/cbc:SellersItemIdentification/cbc:ID',
      type: 'string',
      required: false,
      multiple: false,
    });
    expect(component.collections[0].fields[0].valueXPath)
      .toBe('./cac:Item/cbc:SellersItemIdentification/cbc:ID');
  });

  it('"Ağaçtan seç" formu koruyarak ağaca döner ve seçilen yolla geri açılır', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.openFieldDialog();
    component.draft = { name: 'faturaNo', source: 'XPath', valueXPath: '', type: 'date', required: true, multiple: false };

    component.pickPathFromTree();
    expect(component.fieldDialogOpen).toBe(false);
    expect(component.repickPath).toBe(true);

    component.selectNode(component.findFirst('cbc:ID')!);

    expect(component.fieldDialogOpen).toBe(true);
    expect(component.repickPath).toBe(false);
    expect(component.draft.valueXPath).toBe('/Invoice/cbc:ID');
    // Form korunmalı: yeniden seçim ad/tip bilgisini sıfırlamaz.
    expect(component.draft.name).toBe('faturaNo');
    expect(component.draft.type).toBe('date');
  });

  it('"Ağaçtan seç" modunda dallı öğe seçilebilir ve form geri açılır', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.openFieldDialog();
    component.pickPathFromTree();

    component.selectNode(component.findFirst('cac:InvoiceLine')!);

    expect(component.fieldDialogOpen).toBe(true);
    expect(component.draft.valueXPath).toBe('/Invoice/cac:InvoiceLine');
  });

  it('yaprak öğeye tıklayınca alan ekleme diyaloğu açılır ve yol dolar', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    const node = component.findFirst('cbc:ID')!;

    component.selectNode(node);

    expect(component.fieldDialogOpen).toBe(true);
    expect(component.draft.valueXPath).toBe('/Invoice/cbc:ID');
  });

  it('tekrar eden öğeye tıklayınca diyalog açılmaz, scope dolar', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    const line = component.findFirst('cac:InvoiceLine')!;

    component.selectNode(line);

    expect(component.fieldDialogOpen).toBe(false);
    expect(component.collectionScopeXPath).toBe('/Invoice/cac:InvoiceLine');
  });

  it('saveField alanı ekler ve diyaloğu kapatır', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.openFieldDialog();
    component.draft = { name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false };

    component.saveField();

    expect(component.rules[0].name).toBe('faturaNo');
    expect(component.fieldDialogOpen).toBe(false);
  });

  it('satır alanı düzenlenip güncellenebilir ve silinebilir', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.addCollectionField('satirlar', { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false });

    component.editCollectionField('satirlar', 'Miktar');
    expect(component.fieldDialogOpen).toBe(true);
    expect(component.draftTarget).toBe('satirlar');
    component.draft = { ...component.draft, type: 'decimal' };
    component.saveField();
    expect(component.collections[0].fields[0].type).toBe('decimal');
    expect(component.collections[0].fields.length).toBe(1);

    component.removeCollectionField('satirlar', 'Miktar');
    expect(component.collections[0].fields.length).toBe(0);
  });

  it('alan diyaloğu açılınca hedef her zaman kök fatura alanına döner', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.draftTarget = 'satirlar';

    component.openFieldDialog();

    expect(component.draftTarget).toBe('root');
  });

  it('yaprak öğeye tıklayınca hedef kök fatura alanına döner', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.draftTarget = 'satirlar';

    component.selectNode(component.findFirst('cbc:ID')!);

    expect(component.draftTarget).toBe('root');
  });

  it('satır dizisi oluşturunca hedef koleksiyona sabitlenmez, kökte kalır', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.collectionName = 'satirlar';
    component.collectionScopeXPath = '//cac:InvoiceLine';

    component.addCollectionFromDraft();

    expect(component.draftTarget).toBe('root');
  });

  it('koleksiyon başlığındaki "Diziyi sil" onaydan sonra diziyi kaldırır', () => {
    const component = fixture.componentInstance;
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.setStep(2);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('[data-testid="einvoice-remove-collection-satirlar"]') as HTMLButtonElement;
    expect(button).toBeTruthy();

    vi.stubGlobal('confirm', vi.fn().mockReturnValue(false));
    button.click();
    expect(component.collections.length).toBe(1);

    vi.stubGlobal('confirm', vi.fn().mockReturnValue(true));
    button.click();
    expect(component.collections.length).toBe(0);
  });

  it('tanımlı satır dizisi bütünüyle silinebilir', () => {
    const component = new EInvoiceMappingEditorComponent();
    const emitted: string[] = [];
    component.profileDefinitionChange.subscribe(value => emitted.push(value));
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.addCollectionField('satirlar', { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false });
    component.draftTarget = 'satirlar';
    component.selectedCollectionName = 'satirlar';

    component.removeCollection('satirlar');

    expect(component.collections.length).toBe(0);
    expect(component.draftTarget).toBe('root');
    expect(component.selectedCollectionName).toBe('');
    expect(JSON.parse(emitted.at(-1)!).collections).toEqual([]);
  });

  it('örnek XML yüklenince otomatik 2. adıma geçer', () => {
    const component = new EInvoiceMappingEditorComponent();
    expect(component.activeStep).toBe(1);
    component.loadSampleXml(PREVIEW_SAMPLE);
    expect(component.activeStep).toBe(2);
  });

  it('hedef fatura alanı iken addDraft kök kurala ekler', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.draftTarget = 'root';
    component.draft = { name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false };
    component.addDraft();
    expect(component.rules.length).toBe(1);
    expect(component.rules[0].name).toBe('faturaNo');
  });

  it('hedef satır dizisi iken addDraft alanı koleksiyona ekler', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.draftTarget = 'satirlar';
    component.draft = { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false };
    component.addDraft();
    expect(component.collections[0].fields.length).toBe(1);
    expect(component.collections[0].fields[0].name).toBe('Miktar');
    expect(component.rules.length).toBe(0);
  });

  it('eklenen alanın yanında örnek XML\'den bulunan değer gösterilir', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.addRule({ name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false });

    expect(component.fieldValueText(component.rules[0])).toBe('FTR2026001');
  });

  it('satır alanının yanında ilk satırın değeri gösterilir', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.addCollectionField('satirlar', { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false });

    expect(component.collectionFieldValueText(component.collections[0], component.collections[0].fields[0])).toBe('2');
  });

  it('sihirbaz kapsamları örnek XML yollarını içerir', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);

    expect(component.wizardScopes().some(scope => scope.path === '/Invoice/cbc:ID')).toBe(true);
  });

  it('sihirbazdan gelen XML yolu forma yazılır ve kaynak XPath olur', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.openFieldDialog();
    component.openRegexWizard('fallbackRegex');

    component.applyWizardPath('/Invoice/cbc:IssueDate');

    expect(component.draft.valueXPath).toBe('/Invoice/cbc:IssueDate');
    expect(component.draft.source).toBe('XPath');
    expect(component.wizardTarget).toBeNull();
  });

  it('regex sihirbazı hedef alana desen ve grubu yazar', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.openRegexWizard('fallbackRegex');
    expect(component.wizardTarget).toBe('fallbackRegex');
    component.applyWizardPattern({ pattern: 'TR\\d{24}', group: 'deger' });
    expect(component.draft.fallbackRegex).toBe('TR\\d{24}');
    expect(component.draft.fallbackGroup).toBe('deger');
    expect(component.wizardTarget).toBeNull();
  });

  it('regex sihirbazı regex hedefinde regex/group alanlarına yazar', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.openRegexWizard('regex');
    component.applyWizardPattern({ pattern: 'IBAN[: ]+(?<deger>TR\\d{24})', group: 'deger' });
    expect(component.draft.regex).toBe('IBAN[: ]+(?<deger>TR\\d{24})');
    expect(component.draft.group).toBe('deger');
  });

  it('kaydedilmiş kural önizlemesi bulunan değeri ve eşleşme kaynağını döner', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.addRule({ name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false });
    component.addRule({ name: 'iban', source: 'XPath', valueXPath: '/Invoice/cbc:Yok', fallbackRegex: 'TR\\d{24}', type: 'string', required: false, multiple: false });

    const previews = component.savedRulePreviews();

    expect(previews[0].preview.converted).toBe('FTR2026001');
    expect(previews[0].preview.matchedBy).toBe('xpath');
    expect(previews[1].preview.converted).toBe('TR120001200012345678901234');
    expect(previews[1].preview.matchedBy).toBe('fallback');
  });

  it('koleksiyon önizlemesi ilk satırları tablo verisi olarak döner', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(PREVIEW_SAMPLE);
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.addCollectionField('satirlar', { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false });

    const rows = component.collectionPreviewRows(component.collections[0]);

    expect(rows.length).toBe(2);
    expect(rows[0]['Miktar']).toBe(2);
    expect(component.collectionColumns(component.collections[0])).toEqual(['Miktar']);
  });

  it('örnek XML yokken kural önizlemesi açıklayıcı hata taşır', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.addRule({ name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false });
    expect(component.savedRulePreviews()[0].preview.error).toBe('Örnek XML yüklenmedi.');
  });

  it('koleksiyona eklenen zaten göreceli yol değişmez', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.addCollection('satirlar', '//cac:InvoiceLine');
    component.addCollectionField('satirlar', {
      name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity',
      type: 'decimal', required: false, multiple: false,
    });
    expect(component.collections[0].fields[0].valueXPath).toBe('./cbc:InvoicedQuantity');
  });

  it('liste sihirbazı: keşfedilen listeden seçili kolonlarla koleksiyon oluşturur', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    const lists = component.discoveredLists();
    expect(lists.some(list => list.localName === 'InvoiceLine')).toBe(true);

    component.selectDiscoveredList(lists.find(list => list.localName === 'InvoiceLine')!);
    expect(component.wizardColumns.length).toBeGreaterThan(0);

    component.wizardListName = 'kalemler';
    component.wizardColumns = component.wizardColumns.map(column =>
      column.relativePath === 'cac:Item/cbc:Name' ? { ...column, selected: true, name: 'aciklama' } : { ...column, selected: false });
    component.createListFromWizard();

    expect(component.activeWizardList).toBeNull();
    const collection = component.collections.find(item => item.name === 'kalemler');
    expect(collection).toBeTruthy();
    expect(collection!.fields).toEqual([
      { name: 'aciklama', source: 'XPath', valueXPath: './cac:Item/cbc:Name', type: 'string', required: false, multiple: false },
    ]);
  });

  it('liste sihirbazı: "Böl" işaretli kolon değer + birim iki alan üretir', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    component.selectDiscoveredList(component.discoveredLists().find(list => list.localName === 'InvoiceLine')!);
    component.wizardListName = 'kalemler';
    component.wizardColumns = component.wizardColumns.map(column =>
      column.relativePath === 'cac:Item/cbc:Name'
        ? { ...column, selected: true, name: 'miktar', split: true }
        : { ...column, selected: false });
    component.createListFromWizard();

    const fields = component.collections.find(item => item.name === 'kalemler')!.fields;
    expect(fields.map(field => field.name)).toEqual(['miktar', 'miktarBirim']);
    expect(fields[0]).toMatchObject({ group: 'value', type: 'decimal' });
    expect(fields[1]).toMatchObject({ group: 'unit', type: 'string' });
  });

  it('liste sihirbazı: tümünü seç / tümünü bırak', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    component.selectDiscoveredList(component.discoveredLists().find(list => list.localName === 'InvoiceLine')!);

    component.setAllWizardColumns(false);
    expect(component.wizardColumns.every(column => !column.selected)).toBe(true);
    expect(component.allWizardColumnsSelected()).toBe(false);

    component.setAllWizardColumns(true);
    expect(component.allWizardColumnsSelected()).toBe(true);
  });

  it('liste sihirbazı: çakışan ad uyarır ve oluşturmayı engeller', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    component.selectDiscoveredList(component.discoveredLists().find(list => list.localName === 'InvoiceLine')!);
    component.wizardListName = 'kalemler';
    component.wizardColumns = component.wizardColumns.map(column => ({ ...column, selected: true, name: 'ayniAd' }));

    expect(component.duplicateWizardNames().has('ayniad')).toBe(true);
    expect(component.wizardBlocked()).toBe(true);
    component.createListFromWizard();
    expect(component.collections.find(item => item.name === 'kalemler')).toBeUndefined();
    expect(component.activeWizardList).not.toBeNull();

    component.wizardColumns = component.wizardColumns.map((column, index) => ({ ...column, name: `alan${index}` }));
    expect(component.wizardBlocked()).toBe(false);
    component.createListFromWizard();
    expect(component.collections.find(item => item.name === 'kalemler')).toBeTruthy();
  });

  it('liste sihirbazı: tam ekran aç/kapa ve kapanınca sıfırlanır', () => {
    const component = new EInvoiceMappingEditorComponent();
    component.loadSampleXml(UBL_WITH_TWO_LINES);
    component.selectDiscoveredList(component.discoveredLists().find(list => list.localName === 'InvoiceLine')!);

    expect(component.wizardFullscreen).toBe(false);
    component.toggleWizardFullscreen();
    expect(component.wizardFullscreen).toBe(true);
    component.closeWizard();
    expect(component.wizardFullscreen).toBe(false);
  });
});
