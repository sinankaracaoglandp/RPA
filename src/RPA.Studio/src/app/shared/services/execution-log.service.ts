import { Injectable, computed, signal } from '@angular/core';

export type LogLevel = 'info' | 'success' | 'warn' | 'error' | 'step';

export interface LogEntry {
  /** Monoton artan kimlik (trackBy için). */
  id: number;
  /** İstemci saati (HH:mm:ss). */
  time: string;
  level: LogLevel;
  message: string;
  /** Girdi/çıktı değerleri gibi ek ayrıntı (opsiyonel, JSON/metin). */
  detail?: string;
}

/**
 * Tasarım ekranı footer konsolunu besleyen basit, ekran-yerel çalıştırma günlüğü.
 * Çalıştırma akışı (kaydet → kuyruğa al → durum değişimleri) ve —ileride— per-node
 * giriş/çıkış olayları buraya yazılır. Kalıcı değildir; her yeni çalıştırmada temizlenir.
 */
@Injectable({ providedIn: 'root' })
export class ExecutionLogService {
  private static readonly MaxEntries = 500;
  private nextId = 1;

  private readonly entriesSig = signal<LogEntry[]>([]);
  private readonly visibleSig = signal(false);

  readonly entries = this.entriesSig.asReadonly();
  readonly visible = this.visibleSig.asReadonly();
  readonly count = computed(() => this.entriesSig().length);

  append(level: LogLevel, message: string, detail?: string): void {
    const entry: LogEntry = {
      id: this.nextId++,
      time: new Date().toLocaleTimeString('tr-TR', { hour12: false }),
      level,
      message,
      detail,
    };
    this.entriesSig.update((list) => {
      const next = [...list, entry];
      return next.length > ExecutionLogService.MaxEntries
        ? next.slice(next.length - ExecutionLogService.MaxEntries)
        : next;
    });
  }

  info(message: string, detail?: string): void {
    this.append('info', message, detail);
  }

  step(message: string, detail?: string): void {
    this.append('step', message, detail);
  }

  success(message: string, detail?: string): void {
    this.append('success', message, detail);
  }

  warn(message: string, detail?: string): void {
    this.append('warn', message, detail);
  }

  error(message: string, detail?: string): void {
    this.append('error', message, detail);
  }

  clear(): void {
    this.entriesSig.set([]);
  }

  open(): void {
    this.visibleSig.set(true);
  }

  close(): void {
    this.visibleSig.set(false);
  }

  toggle(): void {
    this.visibleSig.update((v) => !v);
  }
}
