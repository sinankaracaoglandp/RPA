import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SimplifiedToolboxComponent } from './simplified-toolbox.component';
import { ActivityMetadata } from '../../shared/models/activity.model';

const ACTIVITIES: ActivityMetadata[] = [
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

    expect(component.simplifiedActivities().length).toBe(3);
    const items = fixture.nativeElement.querySelectorAll('[data-testid="simplified-activity-item"]');
    expect(items.length).toBe(3);
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

    expect(canvasStub.addNode).toHaveBeenCalledWith('Web.Click');
  });
});
