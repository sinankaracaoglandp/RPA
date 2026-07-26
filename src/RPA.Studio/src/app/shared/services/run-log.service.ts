import { Injectable, inject } from '@angular/core';
import { AuthService } from '../../auth/auth.service';
import {
  SPY_HUB_CONNECTION_FACTORY,
  SPY_HUB_URL,
  SpyHubConnection,
} from './spy.service';
import { ExecutionLogService } from './execution-log.service';

/** RobotHub → StudioHub üzerinden yayınlanan node yürütme olayının Studio tarafı gösterimi. */
export interface NodeLogEvent {
  jobRunId: string;
  nodeId: string;
  nodeType: string;
  activityId?: string | null;
  inputs?: Record<string, string | null> | null;
  outputs?: Record<string, string | null> | null;
  /** Node bittikten sonraki görünür workflow değişkenleri (maskeli anlık görüntü). */
  variables?: Record<string, string | null> | null;
  error?: string | null;
  isBusinessError?: boolean;
}

/**
 * Çalıştırma sırasında orchestrator'dan (StudioHub "NodeLog") gelen canlı node olaylarını
 * dinler ve yalnızca aktif jobRunId'ye ait olanları <see cref="ExecutionLogService"/>'e yazar.
 * Böylece tasarım ekranı footer konsolu adım/giriş/çıkış değerlerini canlı gösterir.
 */
@Injectable({ providedIn: 'root' })
export class RunLogService {
  private readonly auth = inject(AuthService);
  private readonly connectionFactory = inject(SPY_HUB_CONNECTION_FACTORY);
  private readonly log = inject(ExecutionLogService);

  private connection?: SpyHubConnection;
  private activeJobRunId?: string;

  /** İzlenecek çalıştırmayı ayarlar (queue item id = jobRunId). */
  setActiveJobRun(jobRunId: string): void {
    this.activeJobRunId = jobRunId;
  }

  clearActiveJobRun(): void {
    this.activeJobRunId = undefined;
  }

  /** StudioHub bağlantısını (tembel) açar ve "NodeLog" olaylarına abone olur. Idempotent. */
  async connect(): Promise<void> {
    if (this.connection) {
      return;
    }
    const connection = this.connectionFactory(SPY_HUB_URL, () => this.auth.getToken());
    connection.on('NodeLog', (payload) => this.onNodeLog(payload as NodeLogEvent));
    await connection.start();
    this.connection = connection;
  }

  private onNodeLog(evt: NodeLogEvent): void {
    // GUID karşılaştırması harf durumundan bağımsızdır (sunucu/istemci farklı biçimlerde
    // üretebilir); duyarlı karşılaştırma tüm satırları sessizce düşürürdü.
    if (!evt || !this.activeJobRunId ||
      (evt.jobRunId ?? '').toLowerCase() !== this.activeJobRunId.toLowerCase()) {
      return;
    }

    const label = evt.activityId || evt.nodeType;

    const vars = this.format(evt.variables, 'değişkenler');

    if (evt.error) {
      const kind = evt.isBusinessError ? 'İş kuralı' : 'Sistem';
      const detail = [this.format(evt.inputs, 'giriş'), vars].filter((s) => s).join(' · ');
      this.log.error(`✕ ${label} — ${kind}: ${evt.error}`, detail || undefined);
      return;
    }

    // outputs dolu ise tamamlanma olayıdır; değilse başlangıç olayı.
    if (evt.outputs) {
      const detail = [this.format(evt.inputs, 'giriş'), this.format(evt.outputs, 'çıkış'), vars]
        .filter((s) => s)
        .join(' · ');
      this.log.success(`✓ ${label}`, detail || undefined);
    } else {
      this.log.step(`▶ ${label} başladı`);
    }
  }

  private format(values: Record<string, string | null> | null | undefined, prefix: string): string {
    if (!values) {
      return '';
    }
    const parts = Object.entries(values).map(([k, v]) => `${k}=${v ?? '∅'}`);
    return parts.length ? `${prefix}: ${parts.join(', ')}` : '';
  }
}
