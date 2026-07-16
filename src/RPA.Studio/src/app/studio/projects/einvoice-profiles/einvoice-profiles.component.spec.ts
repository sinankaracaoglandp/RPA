import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { EInvoiceMappingEditorComponent } from '../../designer/properties/einvoice-mapping-editor.component';
import { EInvoiceProfilesComponent } from './einvoice-profiles.component';

describe('EInvoiceProfilesComponent', () => {
  let fixture: ComponentFixture<EInvoiceProfilesComponent>;
  let http: HttpTestingController;

  async function configure(routeParams: Record<string, string> = { projectId: 'p1' }): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [EInvoiceProfilesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap(routeParams)) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EInvoiceProfilesComponent);
    http = TestBed.inject(HttpTestingController);
  }

  beforeEach(async () => {
    await configure();
  });

  afterEach(() => http.verify());

  it('lists only profiles for the route project and opens a draft', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects/p1/einvoice-profiles').flush([
      {
        id: 'profile-1',
        projectId: 'p1',
        name: 'Satis',
        description: 'UBL satis faturalari',
        draftDefinitionJson: '{"fields":[],"collections":[]}',
        createdAt: '2026-07-16T00:00:00Z',
      },
    ]);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="einvoice-profile-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Satis');

    fixture.nativeElement.querySelector('[data-testid="edit-profile"]').click();
    http.expectOne('/api/projects/p1/einvoice-profiles/profile-1/versions').flush([]);
    fixture.detectChanges();

    expect(fixture.componentInstance.draftJson()).toContain('"fields"');
  });

  it('presents the page as the e-invoice addressing workspace and passes the selected draft into the editor', () => {
    const draft = '{"fields":[{"name":"faturaNo","source":"XPath","valueXPath":"/Invoice/cbc:ID","type":"string","required":true,"multiple":false}],"collections":[]}';
    fixture.detectChanges();
    http.expectOne('/api/projects/p1/einvoice-profiles').flush([
      {
        id: 'profile-1',
        projectId: 'p1',
        name: 'Satis',
        draftDefinitionJson: draft,
        createdAt: '2026-07-16T00:00:00Z',
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('E-Fatura Adresleme');
    fixture.componentInstance.openDraft('profile-1');
    http.expectOne('/api/projects/p1/einvoice-profiles/profile-1/versions').flush([]);
    fixture.detectChanges();

    const editor = fixture.debugElement.query(By.directive(EInvoiceMappingEditorComponent))
      .componentInstance as EInvoiceMappingEditorComponent;
    expect(editor.rules.map(rule => rule.name)).toEqual(['faturaNo']);
  });

  it('publishes draft and refreshes immutable versions', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects/p1/einvoice-profiles').flush([
      {
        id: 'profile-1',
        projectId: 'p1',
        name: 'Satis',
        draftDefinitionJson: '{"fields":[],"collections":[]}',
        createdAt: '2026-07-16T00:00:00Z',
      },
    ]);
    fixture.detectChanges();

    fixture.componentInstance.openDraft('profile-1');
    http.expectOne('/api/projects/p1/einvoice-profiles/profile-1/versions').flush([
      { id: 'v1', profileId: 'profile-1', version: 1, outputSchemaJson: '{}', publishedAt: '2026-07-16T00:00:00Z' },
    ]);
    fixture.componentInstance.publish('profile-1');

    const publish = http.expectOne('/api/projects/p1/einvoice-profiles/profile-1/publish');
    expect(publish.request.method).toBe('POST');
    publish.flush({
      id: 'v2',
      profileId: 'profile-1',
      version: 2,
      definitionJson: '{"fields":[],"collections":[]}',
      outputSchemaJson: '{}',
      publishedAt: '2026-07-16T01:00:00Z',
    });

    http.expectOne('/api/projects/p1/einvoice-profiles/profile-1/versions').flush([
      { id: 'v2', profileId: 'profile-1', version: 2, outputSchemaJson: '{}', publishedAt: '2026-07-16T01:00:00Z' },
      { id: 'v1', profileId: 'profile-1', version: 1, outputSchemaJson: '{}', publishedAt: '2026-07-16T00:00:00Z' },
    ]);
    fixture.detectChanges();

    const versions = fixture.nativeElement.querySelector('[data-testid="profile-versions"]').textContent;
    expect(versions).toContain('v2');
    expect(versions).toContain('v1');
  });

  it('requires a project selection on the standalone addressing route before creating profiles', async () => {
    TestBed.resetTestingModule();
    await configure({});

    fixture.detectChanges();
    http.expectOne('/api/projects').flush([
      { id: 'p1', name: 'Pilot', workflowCount: 1 },
      { id: 'p2', name: 'Muhasebe', workflowCount: 0 },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Adresleme yapılacak projeyi seç');

    const createButton = fixture.nativeElement.querySelector('[data-testid="create-profile"]') as HTMLButtonElement;
    createButton.click();
    http.expectNone((request) => request.url.includes('/api/projects//einvoice-profiles'));

    const projectSelect = fixture.nativeElement.querySelector('[data-testid="addressing-project-select"]') as HTMLSelectElement;
    projectSelect.value = 'p1';
    projectSelect.dispatchEvent(new Event('change'));

    http.expectOne('/api/projects/p1/einvoice-profiles').flush([]);
    fixture.componentInstance.newName.set('Satış Faturası');
    fixture.detectChanges();
    createButton.click();

    const create = http.expectOne('/api/projects/p1/einvoice-profiles');
    expect(create.request.method).toBe('POST');
    create.flush({
      id: 'profile-1',
      projectId: 'p1',
      name: 'Satış Faturası',
      draftDefinitionJson: '{"fields":[],"collections":[]}',
      createdAt: '2026-07-16T00:00:00Z',
    });
    http.expectOne('/api/projects/p1/einvoice-profiles/profile-1/versions').flush([]);
  });
});
