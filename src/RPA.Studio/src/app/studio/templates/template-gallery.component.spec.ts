import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TemplateGalleryComponent } from './template-gallery.component';
import { TemplateMetadata } from '../../shared/models/template.model';
import { WorkflowDraftService } from '../../shared/services/workflow-draft.service';

const TEMPLATES: TemplateMetadata[] = [
  {
    id: 't1',
    name: 'SAP Order Entry',
    description: 'Creates a sales order',
    icon: '📦',
    category: 'SAP',
    workflowJson: JSON.stringify({
      schemaVersion: '1.0',
      id: 'w1',
      name: 'SAP Order Entry',
      version: '1.0.0',
      nodes: [],
      connections: [],
    }),
  },
  {
    id: 't2',
    name: 'Mail Digest',
    description: 'Summarizes unread mail',
    icon: '📧',
    category: 'Mail',
    workflowJson: JSON.stringify({
      schemaVersion: '1.0',
      id: 'w2',
      name: 'Mail Digest',
      version: '1.0.0',
      nodes: [],
      connections: [],
    }),
  },
];

describe('TemplateGalleryComponent', () => {
  let fixture: ComponentFixture<TemplateGalleryComponent>;
  let component: TemplateGalleryComponent;
  let httpMock: HttpTestingController;

  function flush(templates: TemplateMetadata[] = TEMPLATES): void {
    const req = httpMock.expectOne('/api/templates');
    req.flush(templates);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TemplateGalleryComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(TemplateGalleryComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads templates from the API on init', () => {
    fixture.detectChanges();
    flush();
    fixture.detectChanges();

    expect(component.templates().length).toBe(2);
    const cards = fixture.nativeElement.querySelectorAll('[data-testid="template-card"]');
    expect(cards.length).toBe(2);
  });

  it('filters templates by category', () => {
    fixture.detectChanges();
    flush();
    fixture.detectChanges();

    component.selectCategory('SAP');
    expect(component.filteredTemplates().length).toBe(1);
    expect(component.filteredTemplates()[0].name).toBe('SAP Order Entry');

    component.selectCategory(component.ALL_CATEGORIES);
    expect(component.filteredTemplates().length).toBe(2);
  });

  it('searches templates by name (case-insensitive)', () => {
    fixture.detectChanges();
    flush();
    fixture.detectChanges();

    component.onSearchChange('mail');
    expect(component.filteredTemplates().length).toBe(1);
    expect(component.filteredTemplates()[0].name).toBe('Mail Digest');

    component.onSearchChange('nonexistent');
    expect(component.filteredTemplates().length).toBe(0);
  });

  it('opens the wizard when a template card is selected', () => {
    fixture.detectChanges();
    flush();
    fixture.detectChanges();

    component.openWizard(TEMPLATES[0]);
    fixture.detectChanges();

    expect(component.selectedTemplate()).toEqual(TEMPLATES[0]);
    const wizard = fixture.nativeElement.querySelector('[data-testid="template-wizard-root"]');
    expect(wizard).toBeTruthy();
  });

  it('creates a workflow from template: stores the draft and navigates to the designer', () => {
    fixture.detectChanges();
    flush();
    fixture.detectChanges();

    const draft = TestBed.inject(WorkflowDraftService);
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    const workflow = { schemaVersion: '1.0', id: 'new', name: 'Created', version: '1.0.0', nodes: [], connections: [] };
    component.onWorkflowCreated(workflow);

    expect(draft.consumePending()).toEqual(workflow);
    expect(navigateSpy).toHaveBeenCalledWith('/designer');
    expect(component.selectedTemplate()).toBeNull();
  });

  it('surfaces an error state when template load fails', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/templates');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBe(true);
    expect(fixture.nativeElement.querySelector('[data-testid="template-gallery-error"]')).toBeTruthy();
  });
});
