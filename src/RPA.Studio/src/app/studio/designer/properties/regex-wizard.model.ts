export interface RegexPresetChip {
  id: string;
  label: string;
  pattern: string;
  group: string;
}

/** Örnek XML'de metin taşıyan bir öğe: yolu ve düz metni. */
export interface TextScope {
  path: string;
  text: string;
}

/** Aranan değerin bulunduğu yer: XML yolu, vurgu için bağlam ve üretilmiş desen. */
export interface ValueMatch {
  path: string;
  before: string;
  match: string;
  after: string;
  pattern: string;
  group: string;
}

/** Belgedeki her öğenin KENDİ metnini yolu ile toplar (çocukların metni dahil değil). */
export function collectTextScopes(document: XMLDocument): TextScope[] {
  const scopes: TextScope[] = [];
  const visit = (element: Element, path: string): void => {
    const own = Array.from(element.childNodes)
      .filter(node => node.nodeType === Node.TEXT_NODE)
      .map(node => node.textContent?.trim() ?? '')
      .filter(Boolean)
      .join(' ');
    if (own) scopes.push({ path, text: own });
    for (const child of Array.from(element.children)) visit(child, `${path}/${child.tagName}`);
  };
  if (document.documentElement) visit(document.documentElement, `/${document.documentElement.tagName}`);
  return scopes;
}

/**
 * Kullanıcının yazdığı değeri örnek XML'de arar; bulunduğu her yer için
 * bağlamı ve o değeri yakalayan bir regex önerisini döndürür.
 */
export function findValue(scopes: TextScope[], value: string, context = 30): ValueMatch[] {
  const needle = value.trim();
  if (!needle) return [];
  const results: ValueMatch[] = [];
  for (const scope of scopes) {
    const haystack = scope.text.toLowerCase();
    const lower = needle.toLowerCase();
    let from = 0;
    for (;;) {
      const index = haystack.indexOf(lower, from);
      if (index < 0) break;
      const end = index + needle.length;
      const built = buildPatternFromSelection(scope.text, index, end);
      results.push({
        path: scope.path,
        before: scope.text.slice(Math.max(0, index - context), index),
        match: scope.text.slice(index, end),
        after: scope.text.slice(end, end + context),
        pattern: built.pattern,
        group: built.group,
      });
      if (results.length >= 20) return results;
      from = end;
    }
  }
  return results;
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
