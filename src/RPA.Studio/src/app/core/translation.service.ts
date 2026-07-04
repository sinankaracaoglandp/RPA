import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type SupportedLang = 'tr' | 'en';

const STORAGE_KEY = 'rpa.lang';

@Injectable({ providedIn: 'root' })
export class TranslationService {
  private translations: Record<string, unknown> = {};
  readonly currentLang = signal<SupportedLang>('tr');

  constructor(private http: HttpClient) {}

  async init(): Promise<void> {
    const stored = (localStorage.getItem(STORAGE_KEY) as SupportedLang | null) ?? 'tr';
    await this.use(stored);
  }

  async use(lang: SupportedLang): Promise<void> {
    this.translations = await firstValueFrom(
      this.http.get<Record<string, unknown>>(`assets/i18n/${lang}.json`),
    );
    this.currentLang.set(lang);
    localStorage.setItem(STORAGE_KEY, lang);
  }

  /** Dot-notation key lookup, e.g. "login.title" */
  translate(key: string): string {
    const value = key
      .split('.')
      .reduce<unknown>(
        (acc, part) => (acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined),
        this.translations,
      );
    return typeof value === 'string' ? value : key;
  }
}
