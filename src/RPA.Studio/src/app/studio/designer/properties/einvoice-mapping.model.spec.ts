import {
  EInvoiceMappingRule,
  parseSampleXml,
  previewRule,
  relativizeXPath,
} from './einvoice-mapping.model';

const SAMPLE = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
  <cbc:ID>FTR2026001</cbc:ID>
  <cbc:Note>Odeme IBAN: TR120001200012345678901234</cbc:Note>
  <cbc:Note>Toplam 1.234,56 TL, tarih 16.07.2026</cbc:Note>
</Invoice>`;

function rule(overrides: Partial<EInvoiceMappingRule>): EInvoiceMappingRule {
  return { name: 'alan', source: 'XPath', valueXPath: '', type: 'string', required: false, multiple: false, ...overrides };
}

describe('einvoice-mapping.model fallback ve dönüşüm', () => {
  let document: XMLDocument;
  beforeEach(() => { document = parseSampleXml(SAMPLE).document; });

  it('XPath bulursa matchedBy=xpath döner ve fallback çalışmaz', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:ID', fallbackRegex: 'YANLIS\\d+' }), document);
    expect(preview.converted).toBe('FTR2026001');
    expect(preview.matchedBy).toBe('xpath');
  });

  it('XPath boş dönerse fallback regex ham metinde arar', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:PaymentID', fallbackRegex: 'TR\\d{24}' }), document);
    expect(preview.converted).toBe('TR120001200012345678901234');
    expect(preview.matchedBy).toBe('fallback');
  });

  it('fallback named group ile değer seçer', () => {
    const preview = previewRule(
      rule({ valueXPath: '/Invoice/cbc:Kur', fallbackRegex: 'Toplam (?<deger>\\d{1,3}(?:\\.\\d{3})*,\\d+)', fallbackGroup: 'deger', type: 'decimal' }),
      document,
    );
    expect(preview.converted).toBe(1234.56);
  });

  it('decimal TR binlik/ondalık formatını çevirir', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:X', fallbackRegex: '1\\.234,56', type: 'decimal' }), document);
    expect(preview.converted).toBe(1234.56);
  });

  it('date dd.MM.yyyy formatını ISO değere çevirir', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:X', fallbackRegex: '\\d{2}\\.\\d{2}\\.\\d{4}', type: 'date' }), document);
    expect(preview.converted).toBe('2026-07-16');
  });

  it('ikisi de bulamazsa required alan hata verir', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:Yok', fallbackRegex: 'ASLA\\d+', required: true }), document);
    expect(preview.converted).toBeNull();
    expect(preview.error).toBe('Zorunlu değer bulunamadı.');
  });
});

describe('relativizeXPath', () => {
  it('mutlak scope önekini ./ ile değiştirir', () => {
    expect(relativizeXPath('/Invoice/cac:InvoiceLine/cbc:ID', '/Invoice/cac:InvoiceLine')).toBe('./cbc:ID');
  });
  it('// scope için son segmentten sonrasını göreceler', () => {
    expect(relativizeXPath('/Invoice/cac:InvoiceLine/cac:Item/cbc:Name', '//cac:InvoiceLine')).toBe('./cac:Item/cbc:Name');
  });
  it('zaten göreceli yolu değiştirmez', () => {
    expect(relativizeXPath('./cbc:ID', '//cac:InvoiceLine')).toBe('./cbc:ID');
  });
  it('scope ile eşleşmeyen yolu olduğu gibi bırakır', () => {
    expect(relativizeXPath('/Invoice/cbc:ID', '//cac:InvoiceLine')).toBe('/Invoice/cbc:ID');
  });
});

import {
  discoverLists,
  scanColumns,
  inferType,
  toCamelCase,
  splitValueUnitRules,
  previewProfileDefinition,
} from './einvoice-mapping.model';

const LINES_SAMPLE = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
  xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
  <cac:InvoiceLine>
    <cbc:ID>1</cbc:ID>
    <cbc:InvoicedQuantity unitCode="C62">100</cbc:InvoicedQuantity>
    <cac:Item><cbc:Name>Urun A</cbc:Name></cac:Item>
    <cac:Price><cbc:PriceAmount>12,50</cbc:PriceAmount></cac:Price>
  </cac:InvoiceLine>
  <cac:InvoiceLine>
    <cbc:ID>2</cbc:ID>
    <cbc:InvoicedQuantity unitCode="KGM">5</cbc:InvoicedQuantity>
    <cac:Item><cbc:Name>Urun B</cbc:Name></cac:Item>
    <cac:Price><cbc:PriceAmount>99,00</cbc:PriceAmount></cac:Price>
  </cac:InvoiceLine>
</Invoice>`;

describe('einvoice-mapping.model liste sihirbazı', () => {
  it('toCamelCase prefix atar ve camelCase üretir', () => {
    expect(toCamelCase('cbc:InvoicedQuantity')).toBe('invoicedQuantity');
    expect(toCamelCase('cbc:ID')).toBe('id');
    expect(toCamelCase('unitCode')).toBe('unitCode');
  });

  it('inferType örnek değerden tipi tahmin eder', () => {
    expect(inferType('100')).toBe('integer');
    expect(inferType('12,50')).toBe('decimal');
    expect(inferType('1.234,56')).toBe('decimal');
    expect(inferType('2026-07-16')).toBe('date');
    expect(inferType('16.07.2026')).toBe('date');
    expect(inferType('Urun A')).toBe('string');
  });

  it('discoverLists tekrar eden node grubunu bulur', () => {
    const { tree } = parseSampleXml(LINES_SAMPLE);
    const lists = discoverLists(tree);
    const line = lists.find(item => item.localName === 'InvoiceLine');
    expect(line).toBeTruthy();
    expect(line!.count).toBe(2);
    expect(line!.scopeXPath).toContain('InvoiceLine');
  });

  it('scanColumns iç içe yaprakları ve attribute\'ları göreli yolla listeler', () => {
    const { tree } = parseSampleXml(LINES_SAMPLE);
    const line = discoverLists(tree).find(item => item.localName === 'InvoiceLine')!;
    const columns = scanColumns(line.firstElement);
    const paths = columns.map(column => column.relativePath);
    expect(paths).toContain('cbc:ID');
    expect(paths).toContain('cac:Item/cbc:Name');
    expect(paths).toContain('cac:Price/cbc:PriceAmount');
    expect(paths).toContain('cbc:InvoicedQuantity/@unitCode');
    const quantity = columns.find(column => column.relativePath === 'cbc:InvoicedQuantity');
    expect(quantity!.suggestedName).toBe('invoicedQuantity');
    expect(quantity!.suggestedType).toBe('integer');
    const unit = columns.find(column => column.relativePath === 'cbc:InvoicedQuantity/@unitCode');
    expect(unit!.isAttribute).toBe(true);
    expect(unit!.suggestedName).toBe('unitCode');
  });

  it('splitValueUnitRules değer + birim iki kural üretir', () => {
    const column = { relativePath: 'cbc:Note', sampleValue: '100 ADET', suggestedName: 'miktar', suggestedType: 'string' as const, isAttribute: false };
    const rules = splitValueUnitRules(column, 'miktar');
    expect(rules).toHaveLength(2);
    expect(rules[0]).toMatchObject({ name: 'miktar', group: 'value', type: 'decimal' });
    expect(rules[1]).toMatchObject({ name: 'miktarBirim', group: 'unit', type: 'string' });
    expect(rules[0].valueXPath).toBe('./cbc:Note');
  });

  it('sihirbazdan üretilen koleksiyon previewProfileDefinition ile satırları verir', () => {
    const { tree, document } = parseSampleXml(LINES_SAMPLE);
    const line = discoverLists(tree).find(item => item.localName === 'InvoiceLine')!;
    const collection = {
      name: 'kalemler',
      scopeXPath: line.scopeXPath,
      fields: [
        { name: 'siraNo', source: 'XPath' as const, valueXPath: './cbc:ID', type: 'integer' as const, required: false, multiple: false },
        { name: 'aciklama', source: 'XPath' as const, valueXPath: './cac:Item/cbc:Name', type: 'string' as const, required: false, multiple: false },
      ],
    };
    const preview = previewProfileDefinition({ fields: [], collections: [collection] }, document);
    expect(preview['kalemler']).toEqual([
      { siraNo: 1, aciklama: 'Urun A' },
      { siraNo: 2, aciklama: 'Urun B' },
    ]);
  });
});
