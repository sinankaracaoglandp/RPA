## Task 7: Studio — `ExpressionFunctionService` (katalog istemcisi)

**Files:**
- Create: `src/RPA.Studio/src/app/shared/services/expression-function.service.ts`
- Test: `src/RPA.Studio/src/app/shared/services/expression-function.service.spec.ts`

**Interfaces:**
- Consumes: `GET /api/expression/functions`; Angular `HttpClient` (mevcut servislerin kullandığı desen — `OrchestratorService`/`ActivityCatalogService` nasıl HttpClient enjekte ediyorsa aynısı).
- Produces:
  - `interface ExpressionFunctionParam { name: string; type: string; optional: boolean; }`
  - `interface ExpressionFunctionInfo { name: string; category: string; returnType: string; parameters: ExpressionFunctionParam[]; description: string; example: string; }`
  - `class ExpressionFunctionService` — `load(): Observable<ExpressionFunctionInfo[]>` (bir kez çeker, cache'ler), `filter(prefix: string): ExpressionFunctionInfo[]` (yüklenmiş kataloğu ada göre case-insensitive filtreler).

- [ ] **Step 1: Servis testini yaz (FAIL)**

`expression-function.service.spec.ts` (mevcut servis spec desenini izle — `HttpClientTestingModule`/`provideHttpClientTesting`):

```typescript
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
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-function.service.spec.ts'`
Expected: FAIL — servis yok.

- [ ] **Step 3: Servisi yaz**

`expression-function.service.ts`:

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, shareReplay, tap } from 'rxjs';

export interface ExpressionFunctionParam {
  name: string;
  type: string;
  optional: boolean;
}

export interface ExpressionFunctionInfo {
  name: string;
  category: string;
  returnType: string;
  parameters: ExpressionFunctionParam[];
  description: string;
  example: string;
}

/**
 * İfade fonksiyon kataloğunu backend'den (GET /api/expression/functions) çeker, cache'ler ve
 * autocomplete için ada göre filtre sağlar. Katalog tek kaynak (backend FunctionRegistry).
 */
@Injectable({ providedIn: 'root' })
export class ExpressionFunctionService {
  private readonly http = inject(HttpClient);
  private cache$?: Observable<ExpressionFunctionInfo[]>;
  private loaded: ExpressionFunctionInfo[] = [];

  load(): Observable<ExpressionFunctionInfo[]> {
    if (!this.cache$) {
      this.cache$ = this.http
        .get<ExpressionFunctionInfo[]>('/api/expression/functions')
        .pipe(
          tap((fns) => (this.loaded = fns ?? [])),
          shareReplay(1),
        );
    }
    return this.cache$;
  }

  /** Yüklenmiş kataloğu ada göre (case-insensitive önek) filtreler. */
  filter(prefix: string): ExpressionFunctionInfo[] {
    const q = (prefix ?? '').trim().toLowerCase();
    if (q.length === 0) {
      return [...this.loaded];
    }
    return this.loaded.filter((f) => f.name.toLowerCase().startsWith(q));
  }
}
```

- [ ] **Step 4: Testi çalıştır (PASS)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-function.service.spec.ts'`
Expected: PASS (2 test).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/shared/services/expression-function.service.ts src/RPA.Studio/src/app/shared/services/expression-function.service.spec.ts
git commit -m "feat(studio): ExpressionFunctionService — katalog istemcisi + cache + filtre

GET /api/expression/functions bir kez ceker, shareReplay ile cache'ler, ada gore filtreler.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

