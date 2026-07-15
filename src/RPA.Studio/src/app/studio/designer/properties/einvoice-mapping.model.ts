export interface EInvoiceMappingRule {
  name: string;
  source: 'Standard' | 'XPath' | 'InvoiceNotes' | 'LineNotes';
  scopeXPath?: string | null;
  valueXPath?: string | null;
  regex?: string | null;
  group?: string | null;
  type: 'string' | 'decimal' | 'integer' | 'date' | 'boolean';
  required: boolean;
  multiple: boolean;
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

function valuesFor(rule: EInvoiceMappingRule, document: XMLDocument): string[] {
  let xpath = rule.valueXPath;
  if (!xpath && rule.source === 'InvoiceNotes') xpath = '/*[local-name()="Invoice"]/*[local-name()="Note"]';
  if (!xpath && rule.source === 'LineNotes') xpath = '//*[local-name()="InvoiceLine"]/*[local-name()="Note"]';
  if (!xpath) return [];
  const namespaces = namespaceMap(document);
  let result: XPathResult;
  try {
    result = document.evaluate(namespaceSafeXPath(xpath, namespaces), document, prefix => namespaces.get(prefix ?? '') ?? null, 7);
  } catch {
    // jsdom's XPath 1.0 implementation cannot evaluate namespace-uri(); still use
    // document.evaluate for traversal and apply the namespace-aware path ourselves.
    result = document.evaluate('//*', document, null, 7);
    const requested = xpath.split('/').filter(Boolean).map(segment => segment.replace(/\[.*$/, ''));
    const matches: string[] = [];
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
      if (matchesPath) matches.push(element.textContent?.trim() ?? '');
    }
    return matches;
  }
  const values: string[] = [];
  for (let i = 0; i < result.snapshotLength; i++) values.push(result.snapshotItem(i)?.textContent?.trim() ?? '');
  return values;
}

function convert(value: string, type: EInvoiceMappingRule['type']): unknown {
  if (type === 'string') return value;
  if (type === 'decimal') { const parsed = Number(value.replace(',', '.')); if (!Number.isFinite(parsed)) throw new Error('decimal dönüşümü başarısız.'); return parsed; }
  if (type === 'integer') { if (!/^[+-]?\d+$/.test(value)) throw new Error('integer dönüşümü başarısız.'); return Number.parseInt(value, 10); }
  if (type === 'boolean') { if (/^(true|1)$/i.test(value)) return true; if (/^(false|0)$/i.test(value)) return false; throw new Error('boolean dönüşümü başarısız.'); }
  const date = new Date(value); if (Number.isNaN(date.getTime())) throw new Error('date dönüşümü başarısız.'); return date.toISOString();
}

export function previewRule(rule: EInvoiceMappingRule, document: XMLDocument): RulePreview {
  try {
    let values = valuesFor(rule, document);
    if (rule.regex) {
      const expression = new RegExp(rule.regex);
      values = values.flatMap(value => {
        const match = expression.exec(value);
        if (!match) return [];
        return [rule.group ? (match.groups?.[rule.group] ?? match[Number(rule.group)] ?? '') : (match[0] ?? '')];
      });
    }
    if (!values.length) return { raw: null, converted: rule.multiple ? [] : null, error: rule.required ? 'Zorunlu değer bulunamadı.' : undefined };
    const converted = values.map(value => convert(value, rule.type));
    return { raw: rule.multiple ? values : values[0], converted: rule.multiple ? converted : converted[0] };
  } catch (error) {
    return { raw: null, converted: null, error: error instanceof Error ? error.message : String(error) };
  }
}

export const kurPreset = (): EInvoiceMappingRule => ({ name: 'kur', source: 'InvoiceNotes', valueXPath: '/Invoice/cbc:Note', regex: '(?:KUR|Kur)[:= ]+(?<value>\\d+(?:[.,]\\d+)?)', group: 'value', type: 'decimal', required: false, multiple: false });
export const ibanPreset = (): EInvoiceMappingRule => ({ name: 'iban', source: 'InvoiceNotes', valueXPath: '/Invoice/cbc:Note', regex: 'IBAN[: ]+(?<iban>TR\\d{24})', group: 'iban', type: 'string', required: false, multiple: false });
