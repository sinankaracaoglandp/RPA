import {
  buildPatternFromSelection,
  collectTextScopes,
  explainRegex,
  findValue,
  generalizeSelection,
  REGEX_PRESETS,
} from './regex-wizard.model';

const WIZARD_SAMPLE = `<Invoice xmlns:cbc="urn:cbc">
  <cbc:IssueDate>2026-07-16</cbc:IssueDate>
  <cbc:Note>Odeme IBAN: TR120001200012345678901234 ile yapilacak</cbc:Note>
  <Lines><Line><Code>M-1</Code></Line><Line><Code>M-2</Code></Line></Lines>
</Invoice>`;

describe('collectTextScopes ve findValue', () => {
  let document: XMLDocument;
  beforeEach(() => { document = new DOMParser().parseFromString(WIZARD_SAMPLE, 'application/xml'); });

  it('her öğenin kendi metnini yolu ile toplar', () => {
    const scopes = collectTextScopes(document);
    const date = scopes.find(scope => scope.path.endsWith('IssueDate'));
    expect(date?.text).toBe('2026-07-16');
    expect(date?.path).toBe('/Invoice/cbc:IssueDate');
    // Kök, çocuklarının metnini kendi metni gibi taşımaz.
    expect(scopes.some(scope => scope.path === '/Invoice')).toBe(false);
  });

  it('aranan değeri bulur, XML yolunu ve bağlamı döner', () => {
    const matches = findValue(collectTextScopes(document), 'TR120001200012345678901234');
    expect(matches.length).toBe(1);
    expect(matches[0].path).toBe('/Invoice/cbc:Note');
    expect(matches[0].match).toBe('TR120001200012345678901234');
    expect(matches[0].before).toContain('IBAN:');
  });

  it('bulunan değer için çalışan bir regex üretir', () => {
    const match = findValue(collectTextScopes(document), '2026-07-16')[0];
    const result = new RegExp(match.pattern).exec('2026-07-16');
    expect(result?.groups?.['deger']).toBe('2026-07-16');
  });

  it('değer birden fazla yerde geçiyorsa hepsini döner', () => {
    const matches = findValue(collectTextScopes(document), 'M-');
    expect(matches.map(match => match.path)).toEqual(['/Invoice/Lines/Line/Code', '/Invoice/Lines/Line/Code']);
  });

  it('bulunamayan değer için boş liste döner', () => {
    expect(findValue(collectTextScopes(document), 'YOKBOYLEBIRSEY')).toEqual([]);
  });

  it('boş arama boş liste döner', () => {
    expect(findValue(collectTextScopes(document), '   ')).toEqual([]);
  });
});

describe('regex-wizard.model', () => {
  it('preset listesi IBAN ve Tarih içerir ve desenler derlenebilir', () => {
    const ids = REGEX_PRESETS.map(preset => preset.id);
    expect(ids).toContain('iban');
    expect(ids).toContain('date');
    for (const preset of REGEX_PRESETS) expect(() => new RegExp(preset.pattern)).not.toThrow();
  });

  it('generalizeSelection rakam dizilerini \\d+ ile genelleştirir ve özel karakterleri kaçışlar', () => {
    expect(generalizeSelection('TR12 3456')).toBe('TR\\d+\\s+\\d+');
    expect(generalizeSelection('1.234,56')).toBe('\\d+\\.\\d+,\\d+');
  });

  it('buildPatternFromSelection çapa + named group üretir ve örnek metinde eşleşir', () => {
    const text = 'Odeme IBAN: TR120001200012345678901234 uzerinden';
    const start = text.indexOf('TR12');
    const end = start + 'TR120001200012345678901234'.length;
    const result = buildPatternFromSelection(text, start, end);
    expect(result.group).toBe('deger');
    const match = new RegExp(result.pattern).exec(text);
    expect(match?.groups?.['deger']).toBe('TR120001200012345678901234');
  });

  it('buildPatternFromSelection öneksiz seçimde çapasız desen üretir', () => {
    const text = '16.07.2026 tarihli';
    const result = buildPatternFromSelection(text, 0, 10);
    const match = new RegExp(result.pattern).exec(text);
    expect(match?.groups?.['deger']).toBe('16.07.2026');
  });

  it('explainRegex bilinen yapı taşlarını Türkçe anlatır', () => {
    const explanation = explainRegex('KUR[:= ]+(?<deger>\\d+(?:[.,]\\d+)?)');
    expect(explanation).toContain('rakam');
    expect(explanation).toContain('Parantez');
  });
});
