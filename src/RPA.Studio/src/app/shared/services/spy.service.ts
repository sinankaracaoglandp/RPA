import { Injectable, InjectionToken, inject } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { AuthService } from '../../auth/auth.service';

export type SpyKind = 'sap' | 'web' | 'desktop';

export interface SpyElement {
  sessionId: string;
  kind: SpyKind;
  elementId: string;
  type?: string;
  text?: string;
  enabled?: boolean;
  changeable?: boolean;
  x?: number;
  y?: number;
}

export interface SpyHubConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, handler: (...args: unknown[]) => void): void;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
}

export type SpyHubConnectionFactory = (
  url: string,
  tokenProvider: () => string | null,
) => SpyHubConnection;

export const SPY_HUB_URL = '/hubs/studio';

export const defaultSpyHubConnectionFactory: SpyHubConnectionFactory = (url, tokenProvider) =>
  new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory: () => tokenProvider() ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build() as unknown as SpyHubConnection;

export const SPY_HUB_CONNECTION_FACTORY = new InjectionToken<SpyHubConnectionFactory>(
  'SPY_HUB_CONNECTION_FACTORY',
  { providedIn: 'root', factory: () => defaultSpyHubConnectionFactory },
);

export const SPY_SESSION_ID_FACTORY = new InjectionToken<() => string>(
  'SPY_SESSION_ID_FACTORY',
  { providedIn: 'root', factory: () => () => crypto.randomUUID() },
);

export const SPY_PICK_TIMEOUT_MS = new InjectionToken<number>(
  'SPY_PICK_TIMEOUT_MS',
  { providedIn: 'root', factory: () => 60000 },
);

interface PendingPick {
  sessionId: string;
  resolve: (element: SpyElement) => void;
  reject: (error: Error) => void;
  timeoutId: ReturnType<typeof setTimeout>;
}

@Injectable({ providedIn: 'root' })
export class SpyService {
  private readonly auth = inject(AuthService);
  private readonly connectionFactory = inject(SPY_HUB_CONNECTION_FACTORY);
  private readonly sessionIdFactory = inject(SPY_SESSION_ID_FACTORY);
  private readonly timeoutMs = inject(SPY_PICK_TIMEOUT_MS);

  private connection?: SpyHubConnection;
  private pending?: PendingPick;

  async pick(kind: SpyKind): Promise<SpyElement> {
    if (this.pending) {
      throw new Error('A spy session is already active');
    }

    const connection = await this.ensureConnection();
    const sessionId = this.sessionIdFactory();

    const result = new Promise<SpyElement>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        void this.stopPending(sessionId, reject, new Error('Spy selection timed out'));
      }, this.timeoutMs);

      this.pending = { sessionId, resolve, reject, timeoutId };
    });

    try {
      await connection.invoke('StartSpy', sessionId, kind);
    } catch (error) {
      const active = this.pending as PendingPick | undefined;
      if (active?.sessionId === sessionId) {
        clearTimeout(active.timeoutId);
        this.pending = undefined;
      }
      throw error;
    }

    return result;
  }

  async cancelActive(): Promise<void> {
    const pending = this.pending;
    if (!pending) {
      return;
    }

    await this.stopPending(pending.sessionId, pending.reject, new Error('Spy selection cancelled'));
  }

  private async ensureConnection(): Promise<SpyHubConnection> {
    if (this.connection) {
      return this.connection;
    }

    const connection = this.connectionFactory(SPY_HUB_URL, () => this.auth.getToken());
    connection.on('DetectedElement', (payload) => this.onDetectedElement(payload as SpyElement));
    await connection.start();
    this.connection = connection;
    return connection;
  }

  private onDetectedElement(element: SpyElement): void {
    const pending = this.pending;
    if (!pending || element.sessionId !== pending.sessionId) {
      return;
    }

    clearTimeout(pending.timeoutId);
    this.pending = undefined;
    pending.resolve(element);
  }

  private async stopPending(
    sessionId: string,
    reject: (error: Error) => void,
    error: Error,
  ): Promise<void> {
    const pending = this.pending;
    if (!pending || pending.sessionId !== sessionId) {
      return;
    }

    clearTimeout(pending.timeoutId);
    this.pending = undefined;
    try {
      await this.connection?.invoke('StopSpy', sessionId);
    } finally {
      reject(error);
    }
  }
}
