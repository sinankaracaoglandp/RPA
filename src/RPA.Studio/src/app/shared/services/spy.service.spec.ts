import { TestBed } from '@angular/core/testing';
import {
  SPY_HUB_CONNECTION_FACTORY,
  SPY_PICK_TIMEOUT_MS,
  SPY_SESSION_ID_FACTORY,
  SpyHubConnection,
  SpyService,
} from './spy.service';
import { AuthService } from '../../auth/auth.service';

class FakeSpyConnection implements SpyHubConnection {
  handlers = new Map<string, (...args: unknown[]) => void>();
  invoked: Array<{ method: string; args: unknown[] }> = [];
  started = false;

  async start(): Promise<void> {
    this.started = true;
  }

  async stop(): Promise<void> {
    this.started = false;
  }

  on(methodName: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(methodName, handler);
  }

  async invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invoked.push({ method, args });
    return undefined;
  }

  emit(method: string, ...args: unknown[]): void {
    this.handlers.get(method)?.(...args);
  }
}

class FakeAuth {
  getToken = () => 'jwt-token';
}

async function flushPromises(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
}

describe('SpyService', () => {
  let service: SpyService;
  let connection: FakeSpyConnection;

  beforeEach(() => {
    vi.useFakeTimers();
    connection = new FakeSpyConnection();
    TestBed.configureTestingModule({
      providers: [
        SpyService,
        { provide: AuthService, useClass: FakeAuth },
        { provide: SPY_HUB_CONNECTION_FACTORY, useValue: () => connection },
        { provide: SPY_SESSION_ID_FACTORY, useValue: () => 'session-1' },
        { provide: SPY_PICK_TIMEOUT_MS, useValue: 1000 },
      ],
    });
    service = TestBed.inject(SpyService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts a sap spy session through the hub', async () => {
    const promise = service.pick('sap');
    await flushPromises();

    expect(connection.started).toBe(true);
    // Non-image kind → options JSON null (StartSpy her zaman 3 argümanla çağrılır).
    expect(connection.invoked[0]).toEqual({ method: 'StartSpy', args: ['session-1', 'sap', null] });

    connection.emit('DetectedElement', {
      sessionId: 'session-1',
      kind: 'sap',
      elementId: 'wnd[0]/usr/btn[OK]',
    });
    await expect(promise).resolves.toEqual({
      sessionId: 'session-1',
      kind: 'sap',
      elementId: 'wnd[0]/usr/btn[OK]',
    });
  });

  it('passes image capture options as JSON to the hub', async () => {
    const promise = service.pick('image', { captureMode: 'timer', delaySeconds: 8 });
    await flushPromises();

    expect(connection.invoked[0]).toEqual({
      method: 'StartSpy',
      args: ['session-1', 'image', JSON.stringify({ captureMode: 'timer', delaySeconds: 8 })],
    });

    connection.emit('DetectedElement', {
      sessionId: 'session-1',
      kind: 'image',
      elementId: 'image',
      imageBase64: 'BASE64',
    });
    await promise;
  });

  it('ignores detected elements for another session', async () => {
    const promise = service.pick('sap');
    await flushPromises();

    connection.emit('DetectedElement', {
      sessionId: 'other-session',
      kind: 'sap',
      elementId: 'wnd[0]/usr/btn[WRONG]',
    });
    const assertion = expect(promise).rejects.toThrow('timed out');
    await vi.advanceTimersByTimeAsync(1000);

    await assertion;
    expect(connection.invoked.at(-1)).toEqual({ method: 'StopSpy', args: ['session-1'] });
  });

  it('stops the active session when cancelled', async () => {
    const promise = service.pick('sap');
    await flushPromises();

    await service.cancelActive();

    await expect(promise).rejects.toThrow('cancelled');
    expect(connection.invoked.at(-1)).toEqual({ method: 'StopSpy', args: ['session-1'] });
  });
});
