import { Injectable, signal } from '@angular/core';

export type StudioMode = 'Simple' | 'Advanced';

const STORAGE_KEY = 'rpa-studio-mode';

/**
 * Tracks the designer's Simple/Advanced mode preference (Faz 5, Task 5.5).
 * Persisted in localStorage (MVP — could move to a per-user auth setting later).
 */
@Injectable({ providedIn: 'root' })
export class ModeService {
  private readonly _mode = signal<StudioMode>(this.readInitial());

  readonly mode = this._mode.asReadonly();

  private readInitial(): StudioMode {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored === 'Simple' || stored === 'Advanced' ? stored : 'Advanced';
    } catch {
      return 'Advanced';
    }
  }

  setMode(mode: StudioMode): void {
    this._mode.set(mode);
    try {
      localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      // localStorage unavailable (e.g. privacy mode) — mode still applies in-memory.
    }
  }

  toggle(): void {
    this.setMode(this._mode() === 'Simple' ? 'Advanced' : 'Simple');
  }

  isSimple(): boolean {
    return this._mode() === 'Simple';
  }
}
