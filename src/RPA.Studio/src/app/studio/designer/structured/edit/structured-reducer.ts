import { WorkflowConnection, WorkflowNode, WorkflowVersion } from '../../../../shared/models/workflow.model';
import { ContainerType, StructuredSequence, container, step } from '../structured-model';

export type ReduceResult =
  | { ok: true; tree: StructuredSequence }
  | { ok: false; reason: string };

class ReducerError extends Error {}

const LOOP_TYPES = new Set<ContainerType>(['forEach', 'for', 'while']);
const CONTAINER_TYPES = new Set(['forEach', 'for', 'while', 'if', 'tryCatch']);
const STRUCT_KEYS = new Set(['id', 'type', 'position', 'tryNodeId', 'catchNodeId', 'finallyNodeId']);

/**
 * Keyfi serbest-graf `WorkflowVersion`'ı tek-giriş SESE bölgeleri özyinelemeli indirgeyerek
 * yapısal ağaca çevirir; tam indirgenemezse kesin nedenle reddeder (ya-hep-ya-hiç).
 * Kapsam: dizi + if (yakınsamalı) + döngü. tryCatch → reddedilir (D2).
 */
export function reduceWorkflow(workflow: WorkflowVersion): ReduceResult {
  const byId = new Map(workflow.nodes.map((x) => [x.id, x]));
  const conns = workflow.connections;

  const nonLoop = (c: WorkflowConnection) => c.toPort !== 'loop-back';
  const outEdges = (id: string) => conns.filter((c) => c.from === id && nonLoop(c));
  const outTarget = (id: string, port: string) =>
    conns.find((c) => c.from === id && (c.fromPort ?? 'out') === port && nonLoop(c))?.to ?? null;
  const inCount = (id: string) => conns.filter((c) => c.to === id && nonLoop(c)).length;

  const propsOf = (node: WorkflowNode): Record<string, unknown> => {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(node)) {
      if (!STRUCT_KEYS.has(k) && v !== undefined) { out[k] = v; }
    }
    return out;
  };

  // Ulaşılabilirlik ardılları: bağlantı hedefleri + tryCatch çocuk-head'leri (özellik-bağlı).
  const reachSuccessors = (id: string): string[] => {
    const succ = outEdges(id).map((e) => e.to);
    const node = byId.get(id);
    if (node?.type === 'tryCatch') {
      const rec = node as unknown as Record<string, unknown>;
      for (const key of ['tryNodeId', 'catchNodeId', 'finallyNodeId']) {
        const v = rec[key];
        if (typeof v === 'string') { succ.push(v); }
      }
    }
    return succ;
  };

  // İleri (loop-back hariç) ulaşılabilir küme, opsiyonel durak.
  const forwardReach = (start: string | null, stopAt: string | null): Set<string> => {
    const set = new Set<string>();
    const stack = start ? [start] : [];
    while (stack.length) {
      const id = stack.pop()!;
      if (id === stopAt || set.has(id)) { continue; }
      set.add(id);
      for (const s of reachSuccessors(id)) { stack.push(s); }
    }
    return set;
  };

  // Topolojik indeks (loop-back hariç) — yakınsama sıralaması için.
  const topoIndex = new Map<string, number>();
  {
    const temp = new Set<string>(); const perm = new Set<string>(); const order: string[] = [];
    const visit = (id: string) => {
      if (perm.has(id) || temp.has(id)) { return; }
      temp.add(id);
      for (const s of reachSuccessors(id)) { visit(s); }
      temp.delete(id); perm.add(id); order.push(id);
    };
    for (const nd of workflow.nodes) { visit(nd.id); }
    order.reverse().forEach((id, i) => topoIndex.set(id, i));
  }

  const branchRegion = (head: string | null, conv: string | null): Set<string> =>
    head && head !== conv ? forwardReach(head, conv) : new Set<string>();

  const convergence = (ifId: string): string | null => {
    const tHead = outTarget(ifId, 'true');
    const fHead = outTarget(ifId, 'false');
    const t = forwardReach(tHead, ifId);
    const f = forwardReach(fHead, ifId);
    let conv: string | null = null;
    for (const x of t) {
      if (f.has(x) && (conv === null || (topoIndex.get(x) ?? 0) < (topoIndex.get(conv) ?? 0))) { conv = x; }
    }
    // Doğrulama: dallar ayrık + tek-giriş; conv dışına sızıntı yok.
    const tReg = branchRegion(tHead, conv);
    const fReg = branchRegion(fHead, conv);
    for (const x of tReg) {
      if (fReg.has(x)) { throw new ReducerError(`'${x}' node'u iki daldan ulaşılıyor (yakınsama yok)`); }
    }
    for (const [reg, head] of [[tReg, tHead], [fReg, fHead]] as const) {
      for (const x of reg) {
        for (const c of conns) {
          if (c.to === x && c.toPort !== 'loop-back') {
            const okSource = reg.has(c.from) || (x === head && c.from === ifId);
            if (!okSource) { throw new ReducerError(`'${x}' node'u bölge dışından ulaşılıyor (yakınsama yok)`); }
          }
        }
        for (const e of outEdges(x)) {
          if (!reg.has(e.to) && e.to !== conv) { throw new ReducerError(`'${x}' node'u dal-içinden bölge dışına atlıyor`); }
        }
      }
    }
    return conv;
  };

  const reduceRegion = (entry: string | null, stop: string | null): StructuredSequence => {
    const seq: StructuredSequence = [];
    let cur = entry;
    const seen = new Set<string>();
    while (cur !== null && cur !== stop) {
      if (seen.has(cur)) { throw new ReducerError('Beklenmeyen tekrar (yapısal değil)'); }
      seen.add(cur);
      const node = byId.get(cur);
      if (!node) { throw new ReducerError(`Bilinmeyen node: '${cur}'`); }
      if (node.type === 'tryCatch') {
        const rec = node as unknown as Record<string, unknown>;
        const tryId = rec['tryNodeId'] as string | undefined;
        const catchId = rec['catchNodeId'] as string | undefined;
        let finallyId = rec['finallyNodeId'] as string | undefined;
        // Boş finally konvansiyonu: props'suz 'merge' geçişi → finally boş (merge'ü atla).
        if (finallyId && byId.get(finallyId)?.type === 'merge') {
          finallyId = outTarget(finallyId, 'out') ?? undefined;
        }
        seq.push(container('tryCatch', propsOf(node), {
          success: tryId ? reduceRegion(tryId, null) : [],
          failure: catchId ? reduceRegion(catchId, null) : [],
          out: finallyId ? reduceRegion(finallyId, stop) : [],
        }));
        cur = stop; // tryCatch terminal; devam finally'ye katlandı.
      } else if (LOOP_TYPES.has(node.type as ContainerType)) {
        const bodyHead = outTarget(cur, 'body');
        const body = bodyHead ? reduceRegion(bodyHead, cur) : [];
        seq.push(container(node.type as ContainerType, propsOf(node), { body }));
        cur = outTarget(cur, 'exit');
      } else if (node.type === 'if') {
        const conv = convergence(cur);
        const tHead = outTarget(cur, 'true');
        const fHead = outTarget(cur, 'false');
        seq.push(container('if', propsOf(node), {
          true: tHead && tHead !== conv ? reduceRegion(tHead, conv) : [],
          false: fHead && fHead !== conv ? reduceRegion(fHead, conv) : [],
        }));
        cur = conv;
      } else if (CONTAINER_TYPES.has(node.type)) {
        throw new ReducerError(`Desteklenmeyen node: '${node.type}'`);
      } else {
        const outs = outEdges(cur);
        if (outs.length > 1) { throw new ReducerError(`'${cur}' birden çok çıkışa sahip (yapısal değil)`); }
        const nxt = outs[0]?.to ?? null;
        if (nxt !== null && nxt !== stop && inCount(nxt) > 1) {
          throw new ReducerError(`'${nxt}' node'u birden fazla yerden ulaşılıyor (yakınsama yok)`);
        }
        seq.push(step(node));
        cur = nxt;
      }
    }
    return seq;
  };

  // tryCatch çocukları bağlantı değil node-özelliğidir → bağımsız giriş sayılmaz.
  const childHeads = new Set<string>();
  for (const nd of workflow.nodes) {
    if (nd.type === 'tryCatch') {
      const rec = nd as unknown as Record<string, unknown>;
      for (const key of ['tryNodeId', 'catchNodeId', 'finallyNodeId']) {
        const v = rec[key];
        if (typeof v === 'string') { childHeads.add(v); }
      }
    }
  }

  const hasIncoming = new Set(conns.filter(nonLoop).map((c) => c.to));
  const entries = workflow.nodes.filter((x) => !hasIncoming.has(x.id) && !childHeads.has(x.id));
  if (entries.length !== 1) {
    return { ok: false, reason: entries.length === 0 ? 'Giriş node\'u bulunamadı' : 'Birden fazla giriş node\'u' };
  }
  try {
    return { ok: true, tree: reduceRegion(entries[0].id, null) };
  } catch (e) {
    return { ok: false, reason: e instanceof ReducerError ? e.message : 'Yapısal göç başarısız' };
  }
}
