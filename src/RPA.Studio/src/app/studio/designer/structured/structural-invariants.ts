import { WorkflowVersion } from '../../../shared/models/workflow.model';

const LOOP_TYPES = new Set(['forEach', 'for', 'while']);

/**
 * Düz grafın yapısal değişmezlerini kontrol eder (Studio'da şema doğrulayıcı olmadığından
 * treeToWorkflow çıktısının golden doğrulaması için). İhlal mesajları döner; boş = geçerli.
 */
export function checkStructuralInvariants(workflow: WorkflowVersion): string[] {
  const errors: string[] = [];
  const ids = new Set(workflow.nodes.map((x) => x.id));

  // 1) Bağlantı uçları var olan node'lara işaret etmeli.
  for (const c of workflow.connections) {
    if (!ids.has(c.from)) { errors.push(`baglanti kaynagi eksik: ${c.from}`); }
    if (!ids.has(c.to)) { errors.push(`baglanti hedefi eksik: ${c.to}`); }
  }

  for (const node of workflow.nodes) {
    const out = workflow.connections.filter((c) => c.from === node.id);
    if (LOOP_TYPES.has(node.type)) {
      // 2) Döngü: tam bir body + tam bir loop-back + en fazla bir exit.
      if (!out.some((c) => c.fromPort === 'body')) { errors.push(`${node.type} node ${node.id}: body kenari yok`); }
      if (!workflow.connections.some((c) => c.to === node.id && c.toPort === 'loop-back')) {
        errors.push(`${node.type} node ${node.id}: loop-back kenari yok`);
      }
      if (out.filter((c) => c.fromPort === 'exit').length > 1) { errors.push(`${node.type} node ${node.id}: birden fazla exit`); }
    }
    if (node.type === 'if') {
      // 3) If: true ve false portlarının her biri tam bir kenar taşımalı.
      if (out.filter((c) => c.fromPort === 'true').length !== 1) { errors.push(`if node ${node.id}: tam bir true kenari yok`); }
      if (out.filter((c) => c.fromPort === 'false').length !== 1) { errors.push(`if node ${node.id}: tam bir false kenari yok`); }
    }
    if (node.type === 'tryCatch') {
      // 4) TryCatch: cocuklar ozellik olarak; en az tryNodeId ve finallyNodeId tanimli, gecerli id.
      const rec = node as unknown as Record<string, unknown>;
      for (const key of ['tryNodeId', 'finallyNodeId']) {
        const v = rec[key];
        if (typeof v !== 'string' || !ids.has(v)) { errors.push(`tryCatch node ${node.id}: ${key} gecersiz`); }
      }
      // Cocuklar baglanti olarak yazilmamali.
      if (out.some((c) => ['success', 'failure', 'out'].includes(c.fromPort ?? ''))) {
        errors.push(`tryCatch node ${node.id}: cocuklar baglanti olarak yazilmis`);
      }
    }
  }
  return errors;
}
