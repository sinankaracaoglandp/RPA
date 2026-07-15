import { EInvoiceMappingEditorComponent } from './einvoice-mapping-editor.component';
import { ibanPreset, kurPreset, previewRule } from './einvoice-mapping.model';

const SAMPLE_UBL = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"><cbc:ID>FTR2026</cbc:ID><cbc:PayableAmount>1234.50</cbc:PayableAmount><cbc:Note>IBAN: TR330006100519786457841326</cbc:Note></Invoice>`;
const MAPPING = { name: 'invoiceId', source: 'XPath' as const, valueXPath: '/Invoice/cbc:ID', type: 'string' as const, required: true, multiple: false };
const SCOPED_UBL = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"><cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:Note>first</cbc:Note></cac:InvoiceLine><cac:InvoiceLine><cbc:ID>2</cbc:ID><cbc:Note>second</cbc:Note></cac:InvoiceLine></Invoice>`;

describe('EInvoiceMappingEditorComponent', () => {
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
});
