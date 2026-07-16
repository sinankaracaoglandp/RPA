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
