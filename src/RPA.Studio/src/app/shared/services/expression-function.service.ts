import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, tap } from 'rxjs';

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
