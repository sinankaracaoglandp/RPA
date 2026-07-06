import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActionCenterComponent } from './action-center.component';
import { ActionItem } from '../orchestrator.models';

describe('ActionCenterComponent', () => {
  let fixture: ComponentFixture<ActionCenterComponent>;
  let component: ActionCenterComponent;
  let httpMock: HttpTestingController;

  const items: ActionItem[] = [{
    id: 'a1', type: 'BusinessException', status: 'Pending', jobRunId: null, queueItemId: null,
    assignedUserId: null, resolutionNote: null, resolvedAt: null, timeoutAt: null,
    createdAt: '2026-07-06T10:00:00Z',
  }];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionCenterComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(ActionCenterComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders pending action items', () => {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/action-center').flush(items);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="action-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('BusinessException');
  });

  it('re-queries with type filter', () => {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/action-center').flush([]);

    component.typeFilter = 'Approval';
    component.load();

    const req = httpMock.expectOne((r) => r.url === '/api/action-center');
    expect(req.request.params.get('type')).toBe('Approval');
    req.flush([]);
  });

  it('resolves an item with a note then reloads', () => {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/action-center').flush(items);
    fixture.detectChanges();

    component.startResolve('a1');
    component.resolveNote = 'çözüldü';
    component.confirmResolve('a1');

    const resolveReq = httpMock.expectOne('/api/action-center/a1/resolve');
    expect(resolveReq.request.method).toBe('POST');
    expect(resolveReq.request.body).toEqual({ note: 'çözüldü' });
    resolveReq.flush({ ...items[0], status: 'Resolved' });

    // reload
    httpMock.expectOne((r) => r.url === '/api/action-center').flush([]);
  });
});
