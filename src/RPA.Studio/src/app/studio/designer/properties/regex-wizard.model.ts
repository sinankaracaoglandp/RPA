export interface RegexPresetChip {
  id: string;
  label: string;
  pattern: string;
  group: string;
}

export const REGEX_PRESETS: RegexPresetChip[] = [
  { id: 'date', label: 'Tarih', pattern: '(?<deger>\\d{2}[./]\\d{2}[./]\\d{4}|\\d{4}-\\d{2}-\\d{2})', group: 'deger' },
  { id: 'amount', label: 'Tutar', pattern: '(?<deger>\\d{1,3}(?:\\.\\d{3})*,\\d+|\\d+(?:[.,]\\d+)?)', group: 'deger' },
  { id: 'vkn', label: 'VKN (10 hane)', pattern: '(?<deger>\\b\\d{10}\\b)', group: 'deger' },
  { id: 'tckn', label: 'TCKN (11 hane)', pattern: '(?<deger>\\b\\d{11}\\b)', group: 'deger' },
  { id: 'iban', label: 'IBAN', pattern: '(?<deger>TR\\d{24})', group: 'deger' },
  { id: 'kur', label: 'Kur', pattern: '(?:KUR|Kur|kur)[:= ]+(?<deger>\\d+(?:[.,]\\d+)?)', group: 'deger' },
];

export function escapeRegex(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Seçimi desene çevirir: rakam dizileri \d+, boşluk dizileri \s+, kalan karakterler literal. */
export function generalizeSelection(selection: string): string {
  return escapeRegex(selection)
    .replace(/\d+/g, '\\d+')
    .replace(/[ \t]+/g, '\\s+');
}

/**
 * Seçimin hemen öncesindeki (en çok 20 karakter) sabit metni çapa yapar,
 * seçimi genelleştirip 'deger' adlı gruba koyar.
 */
export function buildPatternFromSelection(text: string, start: number, end: number): { pattern: string; group: string } {
  const selection = text.slice(start, end);
  const prefix = text.slice(Math.max(0, start - 20), start).replace(/^\S*\s/, '').trimStart();
  const anchor = prefix ? `${escapeRegex(prefix).replace(/[ \t]+/g, '\\s+')}\\s*` : '';
  return { pattern: `${anchor}(?<deger>${generalizeSelection(selection.trim())})`, group: 'deger' };
}

/** Desendeki bilinen yapı taşlarını Türkçe cümlelerle açıklar. */
export function explainRegex(pattern: string): string {
  const parts: string[] = [];
  if (/\(\?<[^>]+>/.test(pattern)) parts.push('Parantez içindeki isimli bölüm, alınacak değerdir');
  if (pattern.includes('\\d')) parts.push('\\d bir rakamı temsil eder');
  if (pattern.includes('\\s')) parts.push('\\s bir boşluğu temsil eder');
  if (pattern.includes('\\b')) parts.push('\\b kelime sınırıdır (bitişik rakamları ayırır)');
  if (pattern.includes('+')) parts.push('+ "bir veya daha fazla tekrar" demektir');
  if (pattern.includes('?')) parts.push('? "isteğe bağlı" demektir');
  if (pattern.includes('|')) parts.push('| alternatifler arasında seçim yapar');
  if (pattern.includes('{')) parts.push('{n} tam n tekrar demektir (örn. \\d{10} = 10 rakam)');
  return parts.join('. ');
}
