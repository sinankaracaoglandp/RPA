import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SimplifiedToolboxComponent } from './simplified-toolbox.component';
import { ActivityMetadata } from '../../shared/models/activity.model';

const ACTIVITIES: ActivityMetadata[] = [
  { activityId: 'Web.Goto', displayName: 'Go To URL', category: 'Web' },
  { activityId: 'Web.Click', displayName: 'Click Element', category: 'Web' },
  { activityId: 'Sap.Gui.Connect', displayName: 'Connect to SAP', category: 'SAP' },
  { activityId: 'Mail.Send', displayName: 'Send Mail', category: 'Mail' },
  // Not in the simple-mode subset — should be filtered out.
  { activityId: 'Sap.Gui.SelectTab', displayName: 'Select Tab', category: 'SAP.GUI' },
];

describe('SimplifiedToolboxComponent', () => {
  let fixture: ComponentFixture<SimplifiedToolboxComponent>;
  let component: SimplifiedToolboxComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SimplifiedToolboxComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(SimplifiedToolboxComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads the catalog and shows only the curated simple-mode subset', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/activities');
    req.flush(ACTIVITIES);
    fixture.detectChanges();

    expect(component.simplifiedActivities().length).toBe(4);
    const items = fixture.nativeElement.querySelectorAll('[data-testid="simplified-activity-item"]');
    expect(items.length).toBe(4);
    expect(fixture.nativeElement.textContent).not.toContain('Select Tab');
  });

  it('adds an activity to the canvas when an item is activated', async () => {
    const canvasStub = { addNode: vi.fn().mockResolvedValue('node-1') };
    component.canvas = canvasStub as never;

    fixture.detectChanges();
    const req = httpMock.expectOne('/api/activities');
    req.flush(ACTIVITIES);
    fixture.detectChanges();

    await component.addActivity('Web.Click');

    expect(canvasStub.addNode).toHaveBeenCalledWith('Web.Click', {
      label: 'Click Element',
    });
  });

  it('uses the backend Web.Goto id instead of the legacy Web.Navigate id', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/activities');
    req.flush([
      { activityId: 'Web.Goto', displayName: 'Go To URL', category: 'Web' },
      { activityId: 'Web.Navigate', displayName: 'Legacy Navigate', category: 'Web' },
    ]);
    fixture.detectChanges();

    const ids = component.simplifiedActivities().map((activity) => activity.activityId);
    expect(ids).toContain('Web.Goto');
    expect(ids).not.toContain('Web.Navigate');
  });
});
