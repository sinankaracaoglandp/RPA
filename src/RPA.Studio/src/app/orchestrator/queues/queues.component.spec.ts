import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueuesComponent } from './queues.component';
import { QueueSummary } from '../orchestrator.models';

describe('QueuesComponent', () => {
  let fixture: ComponentFixture<QueuesComponent>;
  let component: QueuesComponent;
  let httpMock: HttpTestingController;

  const queues: QueueSummary[] = [
    { id: 'q1', name: 'Q1', maxRetries: 3, slaSeconds: null, newCount: 2, inProgressCount: 0, failedCount: 1, total: 3 },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QueuesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(QueuesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders queue rows with counts', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/queues').flush(queues);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="queue-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Q1');
  });

  it('loads items when a queue is selected', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/queues').flush(queues);
    fixture.detectChanges();

    component.selectQueue('q1');
    const req = httpMock.expectOne((r) => r.url === '/api/queues/q1/items');
    req.flush({ totalCount: 1, items: [{ id: 'i1', queueId: 'q1', status: 'Failed', attemptCount: 3, assignedRobotId: null, payload: '{}', errorDetail: 'boom' }] });
    fixture.detectChanges();

    const panel = fixture.nativeElement.querySelector('[data-testid="items-panel"]');
    expect(panel).toBeTruthy();
    const itemRows = fixture.nativeElement.querySelectorAll('[data-testid="item-row"]');
    expect(itemRows.length).toBe(1);
    expect(itemRows[0].textContent).toContain('boom');
  });
});
