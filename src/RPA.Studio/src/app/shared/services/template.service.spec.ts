import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TemplateService } from './template.service';
import { TemplateMetadata } from '../models/template.model';

describe('TemplateService', () => {
  let service: TemplateService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TemplateService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches all templates from /api/templates', () => {
    const mock: TemplateMetadata[] = [
      { id: 't1', name: 'SAP Order Entry', category: 'SAP', workflowJson: '{}' },
    ];
    let result: TemplateMetadata[] | undefined;
    service.getTemplates().subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/templates');
    expect(req.request.method).toBe('GET');
    req.flush(mock);

    expect(result).toEqual(mock);
  });

  it('fetches a single template by id', () => {
    const mock: TemplateMetadata = { id: 't1', name: 'SAP Order Entry', category: 'SAP', workflowJson: '{}' };
    let result: TemplateMetadata | undefined;
    service.getTemplate('t1').subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/templates/t1');
    expect(req.request.method).toBe('GET');
    req.flush(mock);

    expect(result).toEqual(mock);
  });
});
