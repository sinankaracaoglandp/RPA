import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PublishWizardComponent } from './publish-wizard.component';
import { ComponentVersion } from '../../../shared/models/component.model';

describe('PublishWizardComponent', () => {
  let component: PublishWizardComponent;
  let fixture: ComponentFixture<PublishWizardComponent>;
  let httpMock: HttpTestingController;

  const mockComponent: ComponentVersion = {
    id: 'id-1',
    componentId: 'comp-1',
    version: '1.0.0',
    displayName: 'Test Component',
    description: 'Test Description',
    author: 'TestAuthor',
    status: 'Draft',
    jsonDefinition: '{}',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PublishWizardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(PublishWizardComponent);
    component = fixture.componentInstance;
    component.component = mockComponent;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('initializes with step 1 (Component Info)', () => {
    expect(component.currentStep()).toBe(1);
  });

  it('displays the correct step title and description', () => {
    fixture.detectChanges();

    const stepTitle = fixture.nativeElement.querySelector('[data-testid="wizard-step-title"]');
    expect(stepTitle).toBeTruthy();
    // Note: actual translation happens in runtime; test just verifies element exists
  });

  it('collects form data for step 1 (name, version, description)', () => {
    component.formData.patchValue({
      name: 'My Component',
      version: '2.0.0',
      description: 'My description',
    });

    expect(component.formData.get('name')?.value).toBe('My Component');
    expect(component.formData.get('version')?.value).toBe('2.0.0');
    expect(component.formData.get('description')?.value).toBe('My description');
  });

  it('navigates between steps with next/previous buttons', () => {
    component.formData.patchValue({
      name: 'Test',
      version: '1.0.0',
      jsonDefinition: '{"test": true}',
    });

    component.nextStep();
    expect(component.currentStep()).toBe(2);

    component.nextStep();
    expect(component.currentStep()).toBe(3);

    component.previousStep();
    expect(component.currentStep()).toBe(2);
  });

  it('disables next button on step 1 if required fields are empty', () => {
    fixture.detectChanges();

    component.formData.patchValue({
      name: '',
      version: '',
    });

    const nextBtn = fixture.nativeElement.querySelector('[data-testid="wizard-next-btn"]');
    expect(nextBtn?.disabled).toBeTruthy();
  });

  it('validates JSON definition on step 2', () => {
    component.currentStep.set(2);

    // Initially valid (default is '{}')
    expect(component.isStep2Valid()).toBeTruthy();

    // Empty JSON becomes invalid
    component.formData.patchValue({
      jsonDefinition: '',
    });
    expect(component.isStep2Valid()).toBeFalsy();

    // Whitespace only also invalid
    component.formData.patchValue({
      jsonDefinition: '   ',
    });
    expect(component.isStep2Valid()).toBeFalsy();

    // Valid JSON
    component.formData.patchValue({
      jsonDefinition: '{"type": "object"}',
    });
    expect(component.isStep2Valid()).toBeTruthy();
  });

  it('publishes component with collected data', () => {
    component.formData.patchValue({
      name: 'Test Component',
      version: '2.0.0',
      description: 'Test Desc',
      jsonDefinition: '{"test": true}',
      testCases: '[]',
    });

    component.publish();

    const req = httpMock.expectOne('/api/components/comp-1/publish');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      version: '2.0.0',
      jsonDefinition: '{"test": true}',
      inputOutputSchema: '{}',
    });

    const publishedComponent: ComponentVersion = {
      id: 'id-1',
      componentId: 'comp-1',
      version: '2.0.0',
      displayName: 'Test Component',
      status: 'Published',
      jsonDefinition: '{"test": true}',
    };

    req.flush(publishedComponent);
    expect(component.successMessage()).toBeTruthy();
  });

  it('displays error message on publish failure', () => {
    component.formData.patchValue({
      name: 'Test Component',
      version: '2.0.0',
      jsonDefinition: '{"test": true}',
    });

    component.publish();

    const req = httpMock.expectOne('/api/components/comp-1/publish');
    req.flush('Invalid JSON', { status: 400, statusText: 'Bad Request' });

    expect(component.errorMessage()).toBeTruthy();
  });

  it('renders all form fields for step 1', () => {
    component.currentStep.set(1);
    fixture.detectChanges();

    const nameInput = fixture.nativeElement.querySelector('[data-testid="wizard-name-input"]');
    const versionInput = fixture.nativeElement.querySelector('[data-testid="wizard-version-input"]');
    const descriptionInput = fixture.nativeElement.querySelector('[data-testid="wizard-description-input"]');

    expect(nameInput).toBeTruthy();
    expect(versionInput).toBeTruthy();
    expect(descriptionInput).toBeTruthy();
  });

  it('renders JSON definition textarea for step 2', () => {
    component.currentStep.set(2);
    fixture.detectChanges();

    const jsonInput = fixture.nativeElement.querySelector('[data-testid="wizard-json-input"]');
    expect(jsonInput).toBeTruthy();
  });

  it('renders test cases textarea for step 3', () => {
    component.currentStep.set(3);
    fixture.detectChanges();

    const testCasesInput = fixture.nativeElement.querySelector('[data-testid="wizard-testcases-input"]');
    expect(testCasesInput).toBeTruthy();
  });

  it('disables previous button on step 1', () => {
    component.currentStep.set(1);
    fixture.detectChanges();

    const prevBtn = fixture.nativeElement.querySelector('[data-testid="wizard-prev-btn"]');
    expect(prevBtn?.disabled).toBeTruthy();
  });

  it('converts next button to publish button on final step', () => {
    component.currentStep.set(3);
    fixture.detectChanges();

    const publishBtn = fixture.nativeElement.querySelector('[data-testid="wizard-publish-btn"]');
    expect(publishBtn).toBeTruthy();
  });

  it('renders wizard with accessible aria labels', () => {
    fixture.detectChanges();

    const modal = fixture.nativeElement.querySelector('[role="dialog"]');
    expect(modal).toBeTruthy();
  });
});
