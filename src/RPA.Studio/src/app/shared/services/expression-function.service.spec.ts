import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ExpressionFunctionService, ExpressionFunctionInfo } from './expression-function.service';

const sample: ExpressionFunctionInfo[] = [
  { name: 'Format', category: 'Tarih', returnType: 'string', parameters: [], description: '', example: 'Format(Now(), "dd.MM.yyyy")' },
  { name: 'Upper', category: 'Metin', returnType: 'string', parameters: [], description: '', example: 'Upper(ad)' },
  { name: 'ToInt', category: 'Dönüşüm', returnType: 'int', parameters: [], description: '', example: 'ToInt(x)' },
];

describe('ExpressionFunctionService', () => {
  let service: ExpressionFunctionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [ExpressionFunctionService, provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ExpressionFunctionService);
    http = TestBed.inject(HttpTestingController);
  });

  it('loads and caches the catalog', () => {
    service.load().subscribe();
    http.expectOne('/api/expression/functions').flush(sample);
    service.load().subscribe(); // ikinci çağrı yeni istek YAPMAMALI
    http.expectNone('/api/expression/functions');
  });

  it('filters by case-insensitive prefix', () => {
    service.load().subscribe();
    http.expectOne('/api/expression/functions').flush(sample);
    expect(service.filter('up').map((f) => f.name)).toEqual(['Upper']);
    expect(service.filter('to').map((f) => f.name)).toEqual(['ToInt']);
    expect(service.filter('').length).toBe(3);
  });

  afterEach(() => http.verify());
});
