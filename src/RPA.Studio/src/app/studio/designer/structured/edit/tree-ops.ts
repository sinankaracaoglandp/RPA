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
