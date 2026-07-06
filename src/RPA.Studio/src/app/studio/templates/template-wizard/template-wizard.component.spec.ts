import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TemplateWizardComponent } from './template-wizard.component';
import { TemplateMetadata } from '../../../shared/models/template.model';
import { WorkflowVersion } from '../../../shared/models/workflow.model';

const TEMPLATE: TemplateMetadata = {
  id: 'tpl-1',
  name: 'SAP Order Entry',
  description: 'Creates a sales order in SAP',
  icon: '📦',
  category: 'SAP',
  workflowJson: JSON.stringify({
    schemaVersion: '1.0',
    id: 'wf-template',
    name: 'SAP Order Entry',
    version: '1.0.0',
    nodes: [{ id: 'n1', type: 'activity', activity: 'Sap.Nco.CallBapi' }],
    connections: [],
  }),
};

describe('TemplateWizardComponent', () => {
  let fixture: ComponentFixture<TemplateWizardComponent>;
  let component: TemplateWizardComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TemplateWizardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TemplateWizardComponent);
    component = fixture.componentInstance;
    component.template = TEMPLATE;
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
  });

  it('starts on step 1 (confirm template) showing template details', () => {
    expect(component.currentStep()).toBe(1);
    const step1 = fixture.nativeElement.querySelector('[data-testid="template-wizard-step-1"]');
    expect(step1.textContent).toContain('SAP Order Entry');
  });

  it('advances through all 3 steps', () => {
    component.nextStep();
    fixture.detectChanges();
    expect(component.currentStep()).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="template-wizard-step-2"]')).toBeTruthy();

    component.nextStep();
    fixture.detectChanges();
    expect(component.currentStep()).toBe(3);
    expect(fixture.nativeElement.querySelector('[data-testid="template-wizard-step-3"]')).toBeTruthy();
  });

  it('pre-fills the customize step with the template name/description', () => {
    expect(component.formData.get('name')?.value).toBe('SAP Order Entry');
    expect(component.formData.get('description')?.value).toBe('Creates a sales order in SAP');
  });

  it('creates a workflow from the template on step 3, applying the customized name', () => {
    let created: WorkflowVersion | undefined;
    component.created.subscribe((wf) => (created = wf));

    component.formData.patchValue({ name: 'My Custom Order Flow' });
    component.nextStep();
    component.nextStep();
    fixture.detectChanges();

    component.create();

    expect(created).toBeTruthy();
    expect(created!.name).toBe('My Custom Order Flow');
    expect(created!.nodes.length).toBe(1);
    expect(created!.nodes[0].activity).toBe('Sap.Nco.CallBapi');
  });

  it('blocks creation and shows an error when the name is cleared', () => {
    component.formData.patchValue({ name: '' });
    component.create();

    expect(component.errorMessage()).toBe(true);
  });

  it('emits close when the wizard is dismissed', () => {
    let closed = false;
    component.close.subscribe(() => (closed = true));

    component.closeWizard();

    expect(closed).toBe(true);
  });
});
