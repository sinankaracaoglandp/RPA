import { buildPatternFromSelection, explainRegex, generalizeSelection, REGEX_PRESETS } from './regex-wizard.model';

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
