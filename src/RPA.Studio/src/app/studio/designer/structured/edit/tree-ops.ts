import {
  ContainerItem, ContainerType, LaneName, StepItem, StructuredItem, StructuredSequence, lanesFor,
} from '../structured-model';

export interface PathStep { lane: LaneName; index: number; }
export interface Path { steps: PathStep[]; index: number; }

/** `steps` ile adreslenen alt-diziyi `fn` ile değiştirir (immutable). steps=[] → kök. */
function updateSeqAt(
  tree: StructuredSequence,
  steps: PathStep[],
  fn: (seq: StructuredSequence) => StructuredSequence,
): StructuredSequence {
  if (steps.length === 0) {
    return fn(tree);
  }
  const [head, ...rest] = steps;
  return tree.map((item, i) => {
    if (i !== head.index || item.kind !== 'container') {
      return item;
    }
    const lane = item.lanes[head.lane] ?? [];
    return { ...item, lanes: { ...item.lanes, [head.lane]: updateSeqAt(lane, rest, fn) } };
  });
}

export function insertItem(
  tree: StructuredSequence, seqSteps: PathStep[], index: number, item: StructuredItem,
): StructuredSequence {
  return updateSeqAt(tree, seqSteps, (seq) => [...seq.slice(0, index), item, ...seq.slice(index)]);
}

export function removeItem(tree: StructuredSequence, path: Path): StructuredSequence {
  return updateSeqAt(tree, path.steps, (seq) => seq.filter((_, i) => i !== path.index));
}

export function moveItem(tree: StructuredSequence, path: Path, delta: number): StructuredSequence {
  return updateSeqAt(tree, path.steps, (seq) => {
    const j = path.index + delta;
    if (j < 0 || j >= seq.length) {
      return seq;
    }
    const next = [...seq];
    const [moved] = next.splice(path.index, 1);
    next.splice(j, 0, moved);
    return next;
  });
}

/**
 * Öğeyi (konteynerse tüm lane içerikleriyle birlikte) derin kopyalar; her adım node'una TAZE id
 * verir. Props/keystroke gibi iç yapılar JSON ile klonlanır — böylece kopya ile özgün node
 * birbirinin değerlerini paylaşmaz (birinde yapılan düzenleme diğerine sızmaz).
 */
export function cloneItem(
  item: StructuredItem, idGen: () => string = () => crypto.randomUUID(),
): StructuredItem {
  if (item.kind === 'step') {
    return { kind: 'step', node: { ...structuredClone(item.node), id: idGen() } };
  }
  const lanes: ContainerItem['lanes'] = {};
  for (const lane of lanesFor(item.type)) {
    const seq = item.lanes[lane];
    if (seq) { lanes[lane] = seq.map((child) => cloneItem(child, idGen)); }
  }
  return { kind: 'container', type: item.type, props: structuredClone(item.props), lanes };
}

/** Öğenin kopyasını hemen ardına ekler; eklenen kopyayı da döndürür (seçim/odak için). */
export function duplicateItem(
  tree: StructuredSequence, path: Path, idGen?: () => string,
): { tree: StructuredSequence; copy: StructuredItem } | null {
  const original = itemAt(tree, path);
  if (!original) { return null; }
  const copy = cloneItem(original, idGen);
  return { tree: insertItem(tree, path.steps, path.index + 1, copy), copy };
}

/** Path'in gösterdiği öğe (yoksa null). */
export function itemAt(tree: StructuredSequence, path: Path): StructuredItem | null {
  let seq = tree;
  for (const s of path.steps) {
    const item = seq[s.index];
    if (!item || item.kind !== 'container') { return null; }
    seq = item.lanes[s.lane] ?? [];
  }
  return seq[path.index] ?? null;
}

/** Öğeyi referans eşitliğiyle ağaçta arar; path'ini döndürür (yoksa null). */
export function findPath(tree: StructuredSequence, target: StructuredItem): Path | null {
  const walk = (seq: StructuredSequence, steps: PathStep[]): Path | null => {
    for (let i = 0; i < seq.length; i++) {
      if (seq[i] === target) {
        return { steps, index: i };
      }
      const item = seq[i];
      if (item.kind === 'container') {
        for (const lane of lanesFor(item.type)) {
          const r = walk(item.lanes[lane] ?? [], [...steps, { lane, index: i }]);
          if (r) { return r; }
        }
      }
    }
    return null;
  };
  return walk(tree, []);
}

export function newStep(activityId: string): StepItem {
  return { kind: 'step', node: { id: crypto.randomUUID(), type: 'activity', activity: activityId } };
}

export function newContainer(type: ContainerType): ContainerItem {
  const lanes: Partial<Record<LaneName, StructuredSequence>> = {};
  for (const lane of lanesFor(type)) { lanes[lane] = []; }
  return { kind: 'container', type, props: {}, lanes };
}

// ---- Sürükle-bırak yardımcıları (C2) ----

/** Bir dizi REFERANSINI ağaçta arar; adım yolunu döndürür (kök = []); yoksa null. */
export function findSeqPath(tree: StructuredSequence, seq: StructuredSequence): PathStep[] | null {
  if (seq === tree) { return []; }
  const walk = (current: StructuredSequence, steps: PathStep[]): PathStep[] | null => {
    for (let i = 0; i < current.length; i++) {
      const item = current[i];
      if (item.kind === 'container') {
        for (const lane of lanesFor(item.type)) {
          const laneSeq = item.lanes[lane] ?? [];
          const here = [...steps, { lane, index: i }];
          if (laneSeq === seq) { return here; }
          const r = walk(laneSeq, here);
          if (r) { return r; }
        }
      }
    }
    return null;
  };
  return walk(tree, []);
}

/** Aynı dizide taşır (CDK moveItemInArray semantiği; ek index ayarı yok). */
export function reorderInSeq(
  tree: StructuredSequence, seqSteps: PathStep[], fromIndex: number, toIndex: number,
): StructuredSequence {
  return updateSeqAt(tree, seqSteps, (seq) => {
    const next = [...seq];
    const [moved] = next.splice(fromIndex, 1);
    next.splice(toIndex, 0, moved);
    return next;
  });
}

/** Diziyi adımlarla dolaşıp döndürür (yardımcı). */
function seqAt(tree: StructuredSequence, steps: PathStep[]): StructuredSequence {
  let seq = tree;
  for (const s of steps) {
    const item = seq[s.index];
    seq = item.kind === 'container' ? (item.lanes[s.lane] ?? []) : [];
  }
  return seq;
}

/**
 * Öğeyi kaynak diziden (fromSteps, fromIndex) hedef diziye (toSteps, toIndex) taşır.
 * Silme, hedef yolu kaynak dizinin ATASINDAN geçiyorsa ve indeks silinenden sonra ise
 * o adımı bir azaltır (index-tabanlı yol tutarlılığı).
 */
export function moveAcross(
  tree: StructuredSequence,
  fromSteps: PathStep[], fromIndex: number,
  toSteps: PathStep[], toIndex: number,
): StructuredSequence {
  const item = seqAt(tree, fromSteps)[fromIndex];
  if (item === undefined) { return tree; }
  const t1 = removeItem(tree, { steps: fromSteps, index: fromIndex });

  const adjusted = toSteps.map((s) => ({ ...s }));
  if (adjusted.length > fromSteps.length
    && fromSteps.every((s, i) => s.lane === adjusted[i].lane && s.index === adjusted[i].index)
    && adjusted[fromSteps.length].index > fromIndex) {
    adjusted[fromSteps.length].index -= 1;
  }
  return insertItem(t1, adjusted, toIndex, item);
}

/** path'teki öğeyi fn ile değiştirir (immutable). */
export function updateItemAt(
  tree: StructuredSequence, path: Path, fn: (item: StructuredItem) => StructuredItem,
): StructuredSequence {
  return updateSeqAt(tree, path.steps, (seq) => seq.map((it, i) => (i === path.index ? fn(it) : it)));
}

/** Öğenin parametrelerini değiştirir: adım → node.properties; konteyner → props. */
/**
 * Öğenin okunabilir adını (`label`) değiştirir. Adım için node üzerinde, konteyner için
 * props üzerinde tutulur — her ikisi de düz grafa node alanı olarak yazılır. Boş ad alanı siler.
 */
export function setItemLabel(
  tree: StructuredSequence, path: Path, label: string,
): StructuredSequence {
  const trimmed = label.trim();
  return updateItemAt(tree, path, (item) => {
    if (item.kind === 'step') {
      const node = { ...item.node };
      if (trimmed) { node.label = trimmed; } else { delete node.label; }
      return { ...item, node };
    }
    const props = { ...item.props };
    if (trimmed) { props['label'] = trimmed; } else { delete props['label']; }
    return { ...item, props };
  });
}

export function setItemProps(
  tree: StructuredSequence, path: Path, props: Record<string, unknown>,
): StructuredSequence {
  return updateItemAt(tree, path, (item) =>
    item.kind === 'step'
      ? { ...item, node: { ...item.node, properties: props } }
      // Konteyner props'u bütün olarak değişir; `label` özellik panelinin alanı değildir,
      // panelden gelen sözlükte yoksa korunur (aksi halde ad düzenlemede silinirdi).
      : { ...item, props: item.props['label'] !== undefined && props['label'] === undefined
        ? { ...props, label: item.props['label'] }
        : props });
}
