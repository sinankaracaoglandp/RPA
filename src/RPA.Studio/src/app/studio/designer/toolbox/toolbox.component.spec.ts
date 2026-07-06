import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivityMetadata } from '../../../shared/models/activity.model';
import { ToolboxComponent } from './toolbox.component';

const ACTIVITIES: ActivityMetadata[] = [
  {
    activityId: 'Web.Click',
    displayName: 'Click Element',
    category: 'Web',
    description: 'Clicks a web element',
  },
  {
    activityId: 'Sap.Gui.SelectTab',
    displayName: 'Select Tab',
    category: 'SAP.GUI',
    description: 'Selects a SAP GUI tab',
  },
  {
    activityId: 'Excel.Read',
    displayName: 'Read Excel',
    category: 'Excel',
    description: 'Reads an Excel range',
  },
];

describe('ToolboxComponent', () => {
  let fixture: ComponentFixture<ToolboxComponent>;
  let component: ToolboxComponent;
  let httpMock: HttpTestingController;

  function flushActivities(activities: ActivityMetadata[] = ACTIVITIES): void {
    const req = httpMock.expectOne('/api/activities');
    req.flush(activities);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ToolboxComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(ToolboxComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads activities from the catalog service on init', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    expect(component.activities().length).toBe(3);
  });

  it('renders an activity item card per activity with name/category/description', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('[data-testid="activity-item"]');
    expect(items.length).toBe(3);
    expect(fixture.nativeElement.textContent).toContain('Click Element');
    expect(fixture.nativeElement.textContent).toContain('Web');
  });

  it('filters activities by search term (case-insensitive, realtime)', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    component.onSearchChange('excel');
    expect(component.filteredActivities().length).toBe(1);
    expect(component.filteredActivities()[0].activityId).toBe('Excel.Read');
  });

  it('shows an empty-state message when search matches nothing', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    component.onSearchChange('does-not-exist');
    fixture.detectChanges();

    const empty = fixture.nativeElement.querySelector('[data-testid="toolbox-empty"]');
    expect(empty).toBeTruthy();
  });

  it('derives category tabs from the loaded activities', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    expect(component.categories()).toEqual(['Excel', 'SAP.GUI', 'Web']);
  });

  it('filters activities by selected category', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    component.selectCategory('SAP.GUI');
    expect(component.filteredActivities().length).toBe(1);
    expect(component.filteredActivities()[0].activityId).toBe('Sap.Gui.SelectTab');
  });

  it('combines search and category filters', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    component.selectCategory('Web');
    component.onSearchChange('excel');
    expect(component.filteredActivities().length).toBe(0);
  });

  it('calls canvas.addNode when an activity is activated', async () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    const addNode = vi.fn().mockResolvedValue('node-1');
    component.canvas = { addNode } as unknown as ToolboxComponent['canvas'];

    await component.addActivity('Web.Click');

    expect(addNode).toHaveBeenCalledWith('Web.Click', {});
  });

  it('emits activityAdded when an activity is added', async () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    const emitted: { activityId: string }[] = [];
    component.activityAdded.subscribe((event) => emitted.push(event));

    await component.addActivity('Excel.Read');

    expect(emitted).toEqual([{ activityId: 'Excel.Read', position: undefined }]);
  });

  it('surfaces a loading error state if the catalog request fails', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/activities');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBe(true);
    const errorEl = fixture.nativeElement.querySelector('[data-testid="toolbox-error"]');
    expect(errorEl).toBeTruthy();
  });

  it('renders the toolbox with an accessible aria-label', () => {
    fixture.detectChanges();
    flushActivities();
    fixture.detectChanges();

    const root = fixture.nativeElement.querySelector('[data-testid="toolbox-root"]');
    expect(root.getAttribute('aria-label')).toBeTruthy();
  });
});
