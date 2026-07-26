import { TestBed } from '@angular/core/testing';
import { RunLogService, NodeLogEvent } from './run-log.service';
import { ExecutionLogService } from './execution-log.service';
import { AuthService } from '../../auth/auth.service';
import { SPY_HUB_CONNECTION_FACTORY, SpyHubConnection } from './spy.service';

/** Hub bağlantısı taklidi — 'NodeLog' handler'ını testin tetikleyebilmesi için saklar. */
class FakeConnection implements SpyHubConnection {
  handlers: Record<string, (payload: unknown) => void> = {};
  on(event: string, handler: (...args: unknown[]) => void): void { this.handlers[event] = handler; }
  async start(): Promise<void> {}
  async stop(): Promise<void> {}
  async invoke(): Promise<unknown> { return undefined; }
}

describe('RunLogService', () => {
  let conn: FakeConnection;
  let svc: RunLogService;
  let log: ExecutionLogService;

  beforeEach(async () => {
    conn = new FakeConnection();
    TestBed.configureTestingModule({
      providers: [
        RunLogService,
        ExecutionLogService,
        { provide: AuthService, useValue: { getToken: () => 't' } },
        { provide: SPY_HUB_CONNECTION_FACTORY, useValue: () => conn },
      ],
    });
    svc = TestBed.inject(RunLogService);
    log = TestBed.inject(ExecutionLogService);
    await svc.connect();
    svc.setActiveJobRun('job-1');
  });

  function emit(evt: Partial<NodeLogEvent>): void {
    conn.handlers['NodeLog']({ jobRunId: 'job-1', nodeId: 'n1', nodeType: 'activity', ...evt });
  }

  it('shows variable snapshot on completion (list<object> dahil)', () => {
    emit({
      activityId: 'Sap.Gui.GridRead',
      outputs: { rows: '[…]' },
      variables: { gridSatirlari: '[{"MATNR":"M-1"}]', sayac: '7' },
    });

    const entry = log.entries().at(-1)!;
    expect(entry.level).toBe('success');
    expect(entry.detail).toContain('değişkenler: gridSatirlari=[{"MATNR":"M-1"}], sayac=7');
  });

  it('shows variable snapshot on error too', () => {
    emit({ activityId: 'Web.Click', error: 'bulunamadı', variables: { url: 'x' } });

    const entry = log.entries().at(-1)!;
    expect(entry.level).toBe('error');
    expect(entry.detail).toContain('değişkenler: url=x');
  });

  it('ignores events from another job run', () => {
    conn.handlers['NodeLog']({ jobRunId: 'other', nodeId: 'n1', nodeType: 'activity', outputs: {} });
    expect(log.entries().length).toBe(0);
  });
});
