import { WorkflowNode, WorkflowVersion } from '../../../shared/models/workflow.model';
import {
  ContainerItem, ContainerType, StructuredSequence, container, step,
} from './structured-model';

const LOOP_TYPES = new Set<ContainerType>(['forEach', 'for', 'while']);
const CONTAINER_TYPES = new Set(['forEach', 'for', 'while', 'if', 'tryCatch']);
// Bir konteyner node'unun WorkflowNode üzerindeki taşınmayan/temizlenecek yapısal anahtarları.
const STRUCT_KEYS = new Set(['id', 'type', 'position', 'tryNodeId', 'catchNodeId', 'finallyNodeId']);

/** Yapısal alt-küme düz grafı yapısal ağaca çevirir (A'nın kendi ürettiği iyi-biçimli graf). */
export function workflowToTree(workflow: WorkflowVersion): StructuredSequence {
  const byId = new Map(workflow.nodes.map((x) => [x.id, x]));
  const conns = workflow.connections;

  // Giriş: loop-back DIŞI hiçbir kenarın hedefi olmayan node (loop-back geri kenardır, giriş sayılmaz).
  const hasIncoming = new Set(conns.filter((c) => c.toPort !== 'loop-back').map((c) => c.to));
  const entry = workflow.nodes.find((x) => !hasIncoming.has(x.id))?.id ?? null;

  const outTarget = (from: string, port: string): string | null =>
    conns.find((c) => c.from === from && (c.fromPort ?? 'out') === port)?.to ?? null;

  const propsOf = (node: WorkflowNode): Record<string, unknown> => {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(node)) {
      if (!STRUCT_KEYS.has(k) && v !== undefined) { out[k] = v; }
    }
    return out;
  };

  const readSequence = (startId: string | null, stopAt: Set<string>): StructuredSequence => {
    const seq: StructuredSequence = [];
    let current = startId;
    const seen = new Set<string>();
    while (current && !stopAt.has(current) && !seen.has(current)) {
      seen.add(current);
      const node = byId.get(current)!;
      if (CONTAINER_TYPES.has(node.type)) {
        const { item, successor } = readContainer(node);
        seq.push(item);
        current = successor;
      } else {
        seq.push(step(node));
        current = outTarget(current, 'out');
      }
    }
    return seq;
  };

  function readContainer(node: WorkflowNode): { item: ContainerItem; successor: string | null } {
    const type = node.type as ContainerType;
    if (LOOP_TYPES.has(type)) {
      const bodyHead = outTarget(node.id, 'body');
      const body = readSequence(bodyHead, new Set([node.id])); // loop-back node'da durur
      const successor = outTarget(node.id, 'exit');
      return { item: container(type, propsOf(node), { body }), successor };
    }
    if (type === 'if') {
      const trueHead = outTarget(node.id, 'true');
      const falseHead = outTarget(node.id, 'false');
      const successor = convergence(node.id);
      const stop = new Set<string>(successor ? [successor] : []);
      return {
        item: container('if', propsOf(node), {
          true: trueHead && trueHead !== successor ? readSequence(trueHead, stop) : [],
          false: falseHead && falseHead !== successor ? readSequence(falseHead, stop) : [],
        }),
        successor,
      };
    }
    // tryCatch ters köprüsü Faz-A kapsamı dışıdır: finally lane'i ile "sonrası" akışı düz grafta
    // contiguous'tur (finally tail'inin 'out' kenarı doğrudan sonraki node'a gider; ayrı sınır
    // işareti yoktur — bkz. BaseRunner.ExecuteTryCatchAsync finally bloğu + spec §3 tryCatch notu).
    throw new Error('workflowToTree: tryCatch ters köprüsü Faz-A kapsamı dışı');
  }

  // Bir node'un yapısal ardılı (dizide sonraki öğenin head'i), konteyner tipine göre.
  function successorOf(id: string): string | null {
    const node = byId.get(id)!;
    if (LOOP_TYPES.has(node.type as ContainerType)) { return outTarget(id, 'exit'); }
    if (node.type === 'if') { return convergence(id); }
    if (node.type === 'tryCatch') { throw new Error('workflowToTree: tryCatch ters köprüsü Faz-A kapsamı dışı'); }
    return outTarget(id, 'out');
  }

  // Başlangıçtan ileri (yapısal ardıl) sıralı zincir. loop-back izlenmez (successorOf 'exit' kullanır).
  function forwardChain(start: string | null): string[] {
    const order: string[] = [];
    let cur = start;
    const seen = new Set<string>();
    while (cur && !seen.has(cur)) { seen.add(cur); order.push(cur); cur = successorOf(cur); }
    return order;
  }

  // If yakınsaması: iki dalın ileri-zincirlerinin ilk ortak node'u = ortak ardıl (successor).
  // Boş dalda outTarget(if, port) doğrudan successor'a işaret eder → zincir orada başlar, hemen kesişir.
  function convergence(ifId: string): string | null {
    const tHead = outTarget(ifId, 'true');
    const fHead = outTarget(ifId, 'false');
    const tChain = tHead ? forwardChain(tHead) : [];
    const fSet = new Set(fHead ? forwardChain(fHead) : []);
    for (const x of tChain) { if (fSet.has(x)) { return x; } }
    return null;
  }

  return readSequence(entry, new Set());
}
