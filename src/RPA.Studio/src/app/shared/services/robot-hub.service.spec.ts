import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import {
  RobotHubConnection,
  RobotHubService,
  ROBOT_HUB_CONNECTION_FACTORY,
} from './robot-hub.service';
import { AuthService } from '../../auth/auth.service';

/** In-memory fake of a SignalR HubConnection. */
class FakeConnection implements RobotHubConnection {
  handlers = new Map<string, (...args: unknown[]) => void>();
  invoked: Array<{ method: string; args: unknown[] }> = [];
  started = false;
  closeCb?: (error?: Error) => void;
  reconnectingCb?: () => void;
  reconnectedCb?: () => void;
  startImpl: () => Promise<void> = async () => {
    this.started = true;
  };

  start(): Promise<void> {
    return this.startImpl();
  }
  async stop(): Promise<void> {
    this.started = false;
  }
  on(method: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(method, handler);
  }
  off(method: string): void {
    this.handlers.delete(method);
  }
  async invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invoked.push({ method, args });
    return undefined;
  }
  onclose(cb: (error?: Error) => void): void {
    this.closeCb = cb;
  }
  onreconnecting(cb: () => void): void {
    this.reconnectingCb = cb;
  }
  onreconnected(cb: () => void): void {
    this.reconnectedCb = cb;
  }

  emit(method: string, ...args: unknown[]): void {
    this.handlers.get(method)?.(...args);
  }
}

class FakeAuth {
  getToken = () => 'jwt-token';
}

describe('RobotHubService', () => {
  let service: RobotHubService;
  let connection: FakeConnection;

  beforeEach(() => {
    connection = new FakeConnection();
    TestBed.configureTestingModule({
      providers: [
        RobotHubService,
        { provide: AuthService, useClass: FakeAuth },
        { provide: ROBOT_HUB_CONNECTION_FACTORY, useValue: () => connection },
      ],
    });
    service = TestBed.inject(RobotHubService);
  });

  it('connects and marks the status connected', async () => {
    await service.connect();
    expect(service.connectionStatus()).toBe('connected');
    expect(service.isConnected()).toBe(true);
    expect(connection.started).toBe(true);
  });

  it('marks disconnected when the connection fails to start', async () => {
    connection.startImpl = async () => {
      throw new Error('boom');
    };
    await expect(service.connect()).rejects.toThrow('boom');
    expect(service.connectionStatus()).toBe('disconnected');
  });

  it('dispatches BreakpointHit events to the observable', async () => {
    await service.connect();
    const promise = firstValueFrom(service.breakpointHit$);
    connection.emit('BreakpointHit', 'job-1', 'node-a', { x: 1 });
    const evt = await promise;
    expect(evt).toEqual({ jobRunId: 'job-1', nodeId: 'node-a', variables: { x: 1 } });
  });

  it('invokes hub commands and strips trailing undefined args', async () => {
    await service.connect();
    await service.setBreakpoint('node-a');
    expect(connection.invoked[0]).toEqual({ method: 'SetBreakpoint', args: ['node-a'] });
  });

  it('rejects commands when not connected', async () => {
    await expect(service.resume()).rejects.toThrow('not connected');
  });

  it('updates status on reconnecting and reconnected callbacks', async () => {
    await service.connect();
    connection.reconnectingCb?.();
    expect(service.connectionStatus()).toBe('reconnecting');
    connection.reconnectedCb?.();
    expect(service.connectionStatus()).toBe('connected');
  });

  it('marks disconnected when the connection closes', async () => {
    await service.connect();
    connection.closeCb?.();
    expect(service.connectionStatus()).toBe('disconnected');
  });
});
