export interface EInvoiceMappingRule {
  name: string;
  source: 'Standard' | 'XPath' | 'InvoiceNotes' | 'LineNotes';
  scopeXPath?: string | null;
  valueXPath?: string | null;
  regex?: string | null;
  group?: string | null;
  fallbackRegex?: string | null;
  fallbackGroup?: string | null;
  type: 'string' | 'decimal' | 'integer' | 'date' | 'boolean';
  required: boolean;
  multiple: boolean;
}

export interface EInvoiceCollectionDefinition {
  name: string;
  scopeXPath: string;
  fields: EInvoiceMappingRule[];
}

export interface EInvoiceProfileDefinition {
  fields: EInvoiceMappingRule[];
  collections: EInvoiceCollectionDefinition[];
}

export interface XmlTreeNode {
  name: string;
  namespaceUri: string | null;
  element: Element;
  parent?: XmlTreeNode;
  children: XmlTreeNode[];
}

export interface RulePreview {
  raw: string | string[] | null;
  converted: unknown;
  matchedBy?: 'xpath' | 'fallback';
  error?: string;
}

function toTree(element: Element, parent?: XmlTreeNode): XmlTreeNode {
  const node: XmlTreeNode = { name: element.tagName, namespaceUri: element.namespaceURI, element, parent, children: [] };
  node.children = Array.from(element.children).map(child => toTree(child, node));
  return node;
}

export function parseSampleXml(xml: string): { document: XMLDocument; tree: XmlTreeNode[] } {
  const document = new DOMParser().parseFromString(xml, 'application/xml');
  if (document.querySelector('parsererror')) throw new Error('Geçersiz XML örneği.');
  return { document, tree: [toTree(document.documentElement)] };
}

export function buildXPath(node: XmlTreeNode): string {
  const segments: string[] = [];
  for (let current: XmlTreeNode | undefined = node; current; current = current.parent) segments.unshift(current.name);
  return `/${segments.join('/')}`;
}

function namespaceMap(document: XMLDocument): Map<string, string> {
  const result = new Map<string, string>();
  for (const attribute of Array.from(document.documentElement.attributes)) {
    if (attribute.name === 'xmlns') result.set('', attribute.value);
    else if (attribute.prefix === 'xmlns') result.set(attribute.localName, attribute.value);
  }
  return result;
}

function namespaceSafeXPath(xpath: string, namespaces: Map<string, string>): string {
  return xpath.split('/').map(segment => {
    if (!segment || segment === '.' || segment === '..' || segment.startsWith('@') || segment.includes('(')) return segment;
    if (segment.includes(':')) {
      const match = /^(?<prefix>[^:]+):(?<name>[^\[]+)(?<predicate>.*)$/.exec(segment);
      const uri = match ? namespaces.get(match.groups!['prefix']) : undefined;
      return match && uri ? `*[local-name()='${match.groups!['name']}' and namespace-uri()='${uri}']${match.groups!['predicate']}` : segment;
    }
    const match = /^(?<name>[^\[]+)(?<predicate>.*)$/.exec(segment);
    return match ? `*[local-name()='${match.groups!['name']}']${match.groups!['predicate']}` : segment;
  }).join('/');
}

function evaluateNodes(xpath: string, context: Node, document: XMLDocument, namespaces: Map<string, string>): Node[] {
  let result: XPathResult;
  try {
    result = document.evaluate(namespaceSafeXPath(xpath, namespaces), context, prefix => namespaces.get(prefix ?? '') ?? null, 7);
  } catch {
    // jsdom's XPath 1.0 implementation cannot evaluate namespace-uri(); still use
    // document.evaluate for traversal and apply the namespace-aware path ourselves.
    result = document.evaluate('descendant-or-self::*', context, null, 7);
    const requested = xpath.replace(/^\.\/?/, '').split('/').filter(Boolean).map(segment => segment.replace(/\[.*$/, ''));
    const matches: Node[] = [];
    for (let i = 0; i < result.snapshotLength; i++) {
      const element = result.snapshotItem(i) as Element | null;
      if (!element) continue;
      const ancestry: Element[] = [];
      for (let current: Element | null = element; current; current = current.parentElement) ancestry.unshift(current);
      const tail = ancestry.slice(-requested.length);
      const matchesPath = tail.length === requested.length && tail.every((candidate, index) => {
        const part = requested[index];
        const colon = part.indexOf(':');
        const prefix = colon >= 0 ? part.slice(0, colon) : '';
        const localName = colon >= 0 ? part.slice(colon + 1) : part;
        return candidate.localName === localName && (!prefix || candidate.namespaceURI === namespaces.get(prefix));
      });
      if (matchesPath) matches.push(element);
    }
    return matches;
  }
  const nodes: Node[] = [];
  for (let i = 0; i < result.snapshotLength; i++) { const node = result.snapshotItem(i); if (node) nodes.push(node); }
  return nodes;
}

function valuesFor(rule: EInvoiceMappingRule, document: XMLDocument): string[] {
  let xpath = rule.valueXPath;
  if (!xpath && rule.source === 'InvoiceNotes') xpath = '/*[local-name()="Invoice"]/*[local-name()="Note"]';
  if (!xpath && rule.source === 'LineNotes') xpath = '//*[local-name()="InvoiceLine"]/*[local-name()="Note"]';
  if (!xpath) return [];
  const namespaces = namespaceMap(document);
  const scopes = rule.scopeXPath ? evaluateNodes(rule.scopeXPath, document, document, namespaces) : [document];
  return scopes.flatMap(scope => evaluateNodes(xpath!, scope, document, namespaces)).map(node => node.textContent?.trim() ?? '');
}

function documentText(root: Node): string {
  const parts: string[] = [];
  const visit = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      const text = node.textContent?.trim();
      if (text) parts.push(text);
    }
    node.childNodes.forEach(visit);
  };
  visit(root);
  return parts.join('\n');
}

function fallbackValues(rule: EInvoiceMappingRule, text: string): string[] {
  const expression = new RegExp(rule.fallbackRegex!, 'g');
  const values: string[] = [];
  for (const match of text.matchAll(expression)) {
    const selected = !rule.fallbackGroup
      ? match[0]
      : /^\d+$/.test(rule.fallbackGroup) ? match[Number(rule.fallbackGroup)] : match.groups?.[rule.fallbackGroup];
    if (selected === undefined) throw new Error(`Fallback regex group '${rule.fallbackGroup}' bulunamadı.`);
    if (selected.trim()) values.push(selected.trim());
    if (!rule.multiple && values.length) break;
  }
  return values;
}

export function relativizeXPath(valueXPath: string, scopeXPath: string): string {
  const value = valueXPath.trim();
  const scope = scopeXPath.trim();
  if (!value || !scope || value.startsWith('.')) return value;
  if (scope.startsWith('//')) {
    const last = scope.split('/').filter(Boolean).pop();
    if (!last) return value;
    const marker = `/${last}/`;
    const index = value.indexOf(marker);
    return index >= 0 ? `./${value.slice(index + marker.length)}` : value;
  }
  return value.startsWith(`${scope}/`) ? `./${value.slice(scope.length + 1)}` : value;
}

function convert(value: string, type: EInvoiceMappingRule['type']): unknown {
  if (type === 'string') return value;
  if (type === 'decimal') {
    let normalized = value.trim();
    if (normalized.includes(',') && normalized.includes('.')) {
      normalized = normalized.lastIndexOf(',') > normalized.lastIndexOf('.')
        ? normalized.split('.').join('').replace(',', '.')
        : normalized.split(',').join('');
    } else {
      normalized = normalized.replace(',', '.');
    }
    const parsed = Number(normalized);
    if (!Number.isFinite(parsed)) throw new Error('decimal dönüşümü başarısız.');
    return parsed;
  }
  if (type === 'integer') { if (!/^[+-]?\d+$/.test(value)) throw new Error('integer dönüşümü başarısız.'); return Number.parseInt(value, 10); }
  if (type === 'boolean') { if (/^(true|1)$/i.test(value)) return true; if (/^(false|0)$/i.test(value)) return false; throw new Error('boolean dönüşümü başarısız.'); }
  return convertDate(value);
}

function convertDate(value: string): string {
  const iso = /^(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})$/.exec(value.trim());
  const tr = /^(?<day>\d{2})[./](?<month>\d{2})[./](?<year>\d{4})$/.exec(value.trim());
  const match = iso ?? tr;
  if (!match) throw new Error('date dönüşümü başarısız.');
  const year = Number(match.groups!['year']);
  const month = Number(match.groups!['month']);
  const day = Number(match.groups!['day']);
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (year === 0 || month < 1 || month > 12 || day < 1 || day > days[month - 1]) throw new Error('date dönüşümü başarısız.');
  return `${match.groups!['year'].padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

export function previewRule(rule: EInvoiceMappingRule, document: XMLDocument): RulePreview {
  try {
    let values = valuesFor(rule, document);
    if (rule.regex) {
      const expression = new RegExp(rule.regex);
      values = values.flatMap(value => {
        const match = expression.exec(value);
        if (!match) return [];
        if (!rule.group) return [match[0] ?? ''];
        const selected = /^\d+$/.test(rule.group) ? match[Number(rule.group)] : match.groups?.[rule.group];
        if (selected === undefined) throw new Error(`Regex group '${rule.group}' bulunamadı.`);
        return [selected];
      });
    }
    let matchedBy: RulePreview['matchedBy'] = values.length ? 'xpath' : undefined;
    if (!values.length && rule.fallbackRegex) {
      values = fallbackValues(rule, documentText(document.documentElement));
      if (values.length) matchedBy = 'fallback';
    }
    if (!values.length) return { raw: null, converted: rule.multiple ? [] : null, error: rule.required ? 'Zorunlu değer bulunamadı.' : undefined };
    const converted = values.map(value => convert(value, rule.type));
    return { raw: rule.multiple ? values : values[0], converted: rule.multiple ? converted : converted[0], matchedBy };
  } catch (error) {
    return { raw: null, converted: null, error: error instanceof Error ? error.message : String(error) };
  }
}

export function previewProfileDefinition(definition: EInvoiceProfileDefinition, document: XMLDocument): Record<string, any> {
  const namespaces = namespaceMap(document);
  const result: Record<string, any> = {};
  for (const field of definition.fields) result[field.name] = previewRule(field, document).converted;
  for (const collection of definition.collections) {
    result[collection.name] = evaluateNodes(collection.scopeXPath, document, document, namespaces)
      .map(scope => {
        const row: Record<string, unknown> = {};
        for (const field of collection.fields) row[field.name] = previewRuleInScope(field, scope, document, namespaces).converted;
        return row;
      });
  }
  return result;
}

function previewRuleInScope(rule: EInvoiceMappingRule, scope: Node, document: XMLDocument, namespaces: Map<string, string>): RulePreview {
  try {
    const xpath = rule.valueXPath || (rule.source === 'LineNotes' ? './*[local-name()="Note"]' : '');
    if (!xpath) return { raw: null, converted: rule.multiple ? [] : null, error: rule.required ? 'Zorunlu deÄŸer bulunamadÄ±.' : undefined };
    let values = evaluateNodes(xpath, scope, document, namespaces).map(node => node.textContent?.trim() ?? '').filter(Boolean);
    if (rule.regex) {
      const expression = new RegExp(rule.regex);
      values = values.flatMap(value => {
        const match = expression.exec(value);
        if (!match) return [];
        if (!rule.group) return [match[0] ?? ''];
        const selected = /^\d+$/.test(rule.group) ? match[Number(rule.group)] : match.groups?.[rule.group];
        if (selected === undefined) throw new Error(`Regex group '${rule.group}' bulunamadÄ±.`);
        return [selected];
      });
    }
    let matchedBy: RulePreview['matchedBy'] = values.length ? 'xpath' : undefined;
    if (!values.length && rule.fallbackRegex) {
      values = fallbackValues(rule, documentText(scope));
      if (values.length) matchedBy = 'fallback';
    }
    if (!values.length) return { raw: null, converted: rule.multiple ? [] : null, error: rule.required ? 'Zorunlu deÄŸer bulunamadÄ±.' : undefined };
    const converted = values.map(value => convert(value, rule.type));
    return { raw: rule.multiple ? values : values[0], converted: rule.multiple ? converted : converted[0], matchedBy };
  } catch (error) {
    return { raw: null, converted: null, error: error instanceof Error ? error.message : String(error) };
  }
}

// --- Liste (satır) sihirbazı yardımcıları -------------------------------------------------

/** Örnek XML'de tekrar eden bir node grubu — satır dizisi adayı. */
export interface DiscoveredList {
  /** Yerel ad (prefix'siz), örn. "InvoiceLine". */
  localName: string;
  /** Kullanıcıya gösterilen tam ad (prefix'li), örn. "cac:InvoiceLine". */
  displayName: string;
  /** Tam scope XPath'i (ilk örneğin yolu). */
  scopeXPath: string;
  /** Kaç satır (kaç kez tekrar ediyor). */
  count: number;
  /** İlk örnek — kolon taraması için. */
  firstElement: Element;
}

/** Keşif tablosundaki bir aday kolon. */
export interface DiscoveredColumn {
  /** Scope'a göreli yol, örn. "cbc:ID", "cac:Item/cbc:Name", "cbc:InvoicedQuantity/@unitCode". */
  relativePath: string;
  /** İlk satırdaki örnek değer. */
  sampleValue: string;
  /** Önerilen değişken adı (camelCase). */
  suggestedName: string;
  /** Örnek değerden tahmin edilen tip. */
  suggestedType: EInvoiceMappingRule['type'];
  isAttribute: boolean;
}

function localPart(qualifiedName: string): string {
  const colon = qualifiedName.lastIndexOf(':');
  return colon >= 0 ? qualifiedName.slice(colon + 1) : qualifiedName;
}

/** "InvoicedQuantity" → "invoicedQuantity"; "ID" → "id"; "unitCode" → "unitCode". */
export function toCamelCase(name: string): string {
  const local = localPart(name);
  if (!local) return local;
  if (/^[A-Z0-9]+$/.test(local)) return local.toLowerCase();
  return local.charAt(0).toLowerCase() + local.slice(1);
}

/** Örnek değerden alan tipini tahmin eder. */
export function inferType(sample: string): EInvoiceMappingRule['type'] {
  const value = sample.trim();
  if (!value) return 'string';
  if (/^\d{4}-\d{2}-\d{2}$/.test(value) || /^\d{2}[./]\d{2}[./]\d{4}$/.test(value)) return 'date';
  if (/^[+-]?\d+$/.test(value)) return 'integer';
  if (/^[+-]?\d{1,3}([.,]\d{3})*([.,]\d+)?$/.test(value) && /[.,]/.test(value)) return 'decimal';
  if (/^[+-]?\d+[.,]\d+$/.test(value)) return 'decimal';
  return 'string';
}

/** Tekrar eden (aynı ebeveyn altında aynı adla 2+ kez geçen) node gruplarını bulur. */
export function discoverLists(tree: XmlTreeNode[]): DiscoveredList[] {
  const results: DiscoveredList[] = [];
  const visit = (nodes: XmlTreeNode[]): void => {
    const groups = new Map<string, XmlTreeNode[]>();
    for (const node of nodes) {
      const key = node.name;
      (groups.get(key) ?? groups.set(key, []).get(key)!).push(node);
    }
    for (const [name, members] of groups) {
      if (members.length >= 2) {
        results.push({
          localName: localPart(name),
          displayName: name,
          scopeXPath: buildXPath(members[0]),
          count: members.length,
          firstElement: members[0].element,
        });
      }
    }
    for (const node of nodes) visit(node.children);
  };
  visit(tree);
  return results;
}

/** Bir liste örneğinin altındaki tüm yaprak alanlarını ve attribute'larını tarar. */
export function scanColumns(firstElement: Element): DiscoveredColumn[] {
  const columns: DiscoveredColumn[] = [];
  const seen = new Set<string>();
  const push = (relativePath: string, sampleValue: string, isAttribute: boolean): void => {
    if (seen.has(relativePath)) return;
    seen.add(relativePath);
    columns.push({
      relativePath,
      sampleValue,
      suggestedName: toCamelCase(relativePath.split('/').pop()!.replace(/^@/, '')),
      suggestedType: inferType(sampleValue),
      isAttribute,
    });
  };
  const visit = (element: Element, prefix: string): void => {
    for (const attribute of Array.from(element.attributes)) {
      if (attribute.name === 'xmlns' || attribute.prefix === 'xmlns') continue;
      push(`${prefix}@${attribute.name}`, attribute.value, true);
    }
    if (element.children.length === 0) {
      push(prefix.replace(/\/$/, ''), element.textContent?.trim() ?? '', false);
      return;
    }
    for (const child of Array.from(element.children)) visit(child, `${prefix}${child.tagName}/`);
  };
  for (const child of Array.from(firstElement.children)) visit(child, `${child.tagName}/`);
  // Kök elemanın kendi attribute'ları (örn. satırın doğrudan @unitCode'u yoksa da tutarlılık için)
  for (const attribute of Array.from(firstElement.attributes)) {
    if (attribute.name === 'xmlns' || attribute.prefix === 'xmlns') continue;
    push(`@${attribute.name}`, attribute.value, true);
  }
  return columns;
}

/** Seçili bir kolonu koleksiyon alan kuralına çevirir (scope'a göreli). */
export function columnToRule(column: DiscoveredColumn, name: string, type: EInvoiceMappingRule['type']): EInvoiceMappingRule {
  return { name, source: 'XPath', valueXPath: `./${column.relativePath}`, type, required: false, multiple: false };
}

/** "100 ADET" gibi birleşik değeri sayı + birim iki kurala böler. */
export function splitValueUnitRules(column: DiscoveredColumn, name: string): EInvoiceMappingRule[] {
  const pattern = '(?<value>[\\d.,]+)\\s*(?<unit>\\D+)';
  return [
    { name, source: 'XPath', valueXPath: `./${column.relativePath}`, regex: pattern, group: 'value', type: 'decimal', required: false, multiple: false },
    { name: `${name}Birim`, source: 'XPath', valueXPath: `./${column.relativePath}`, regex: pattern, group: 'unit', type: 'string', required: false, multiple: false },
  ];
}

export const kurPreset = (): EInvoiceMappingRule => ({ name: 'kur', source: 'InvoiceNotes', valueXPath: '/Invoice/cbc:Note', regex: '(?:KUR|Kur)[:= ]+(?<value>\\d+(?:[.,]\\d+)?)', group: 'value', type: 'decimal', required: false, multiple: false });
export const ibanPreset = (): EInvoiceMappingRule => ({ name: 'iban', source: 'InvoiceNotes', valueXPath: '/Invoice/cbc:Note', regex: 'IBAN[: ]+(?<iban>TR\\d{24})', group: 'iban', type: 'string', required: false, multiple: false });
