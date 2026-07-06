import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentLibraryComponent } from './component-library.component';
import { ComponentVersion } from '../../shared/models/component.model';

const MOCK_COMPONENTS: ComponentVersion[] = [
  {
    id: 'id-1',
    componentId: 'comp-1',
    version: '1.0.0',
    displayName: 'SAP Login',
    description: 'Login to SAP system',
    author: 'DevTeam',
    category: 'SAP',
    status: 'Published',
    jsonDefinition: '{}',
  },
  {
    id: 'id-2',
    componentId: 'comp-2',
    version: '1.1.0',
    displayName: 'Excel Reader',
    description: 'Read Excel files',
    author: 'DataTeam',
    category: 'Excel',
    status: 'Draft',
    jsonDefinition: '{}',
  },
];

describe('ComponentLibraryComponent', () => {
  let component: ComponentLibraryComponent;
  let fixture: ComponentFixture<ComponentLibraryComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComponentLibraryComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(ComponentLibraryComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads components from API on init', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    expect(component.components().length).toBe(2);
  });

  it('renders a component card per component with name, version, author, status', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('[data-testid="component-card"]');
    expect(cards.length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('SAP Login');
    expect(fixture.nativeElement.textContent).toContain('Published');
  });

  it('filters components by status (All/Draft/Published)', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    component.selectFilter('Published');
    expect(component.filteredComponents().length).toBe(1);
    expect(component.filteredComponents()[0].displayName).toBe('SAP Login');

    component.selectFilter('Draft');
    expect(component.filteredComponents().length).toBe(1);
    expect(component.filteredComponents()[0].displayName).toBe('Excel Reader');

    component.selectFilter('All');
    expect(component.filteredComponents().length).toBe(2);
  });

  it('filters components by search term (case-insensitive)', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    component.onSearchChange('sap');
    expect(component.filteredComponents().length).toBe(1);
    expect(component.filteredComponents()[0].displayName).toBe('SAP Login');

    component.onSearchChange('excel');
    expect(component.filteredComponents().length).toBe(1);
    expect(component.filteredComponents()[0].displayName).toBe('Excel Reader');

    component.onSearchChange('missing');
    expect(component.filteredComponents().length).toBe(0);
  });

  it('surfaces loading error state if component fetch fails', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBe(true);
    const errorEl = fixture.nativeElement.querySelector('[data-testid="component-library-error"]');
    expect(errorEl).toBeTruthy();
  });

  it('renders the library with accessible aria-label', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    const root = fixture.nativeElement.querySelector('[data-testid="component-library-root"]');
    expect(root.getAttribute('aria-label')).toBeTruthy();
  });

  it('selects and shows component details in a modal/panel on card click', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="component-card"]');
    card.click();
    fixture.detectChanges();

    expect(component.selectedComponent()).toBe(MOCK_COMPONENTS[0]);
    const detailsPanel = fixture.nativeElement.querySelector('[data-testid="component-details-panel"]');
    expect(detailsPanel).toBeTruthy();
  });

  it('closes details panel on modal close action', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    component.selectedComponent.set(MOCK_COMPONENTS[0]);
    fixture.detectChanges();

    component.closeDetails();
    fixture.detectChanges();

    expect(component.selectedComponent()).toBeNull();
    const detailsPanel = fixture.nativeElement.querySelector('[data-testid="component-details-panel"]');
    expect(detailsPanel).toBeFalsy();
  });

  it('approves a published component', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    component.selectedComponent.set(MOCK_COMPONENTS[0]);
    fixture.detectChanges();

    component.approveComponent();

    const approveReq = httpMock.expectOne(`/api/components/${MOCK_COMPONENTS[0].componentId}/${MOCK_COMPONENTS[0].version}/approve`);
    expect(approveReq.request.method).toBe('POST');

    approveReq.flush(MOCK_COMPONENTS[0]);
    expect(component.approveSuccess()).toBeTruthy();
  });

  it('shows error message on approve failure', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/components');
    req.flush(MOCK_COMPONENTS);
    fixture.detectChanges();

    component.selectedComponent.set(MOCK_COMPONENTS[0]);
    fixture.detectChanges();

    component.approveComponent();

    const approveReq = httpMock.expectOne(`/api/components/${MOCK_COMPONENTS[0].componentId}/${MOCK_COMPONENTS[0].version}/approve`);
    approveReq.flush('Unauthorized', { status: 403, statusText: 'Forbidden' });

    expect(component.approveError()).toBeTruthy();
  });
});
