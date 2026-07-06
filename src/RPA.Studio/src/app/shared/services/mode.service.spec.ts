import { TestBed } from '@angular/core/testing';
import { ModeService } from './mode.service';

describe('ModeService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('defaults to Advanced mode when nothing is persisted', () => {
    const service = TestBed.inject(ModeService);
    expect(service.mode()).toBe('Advanced');
    expect(service.isSimple()).toBe(false);
  });

  it('setMode switches mode and persists to localStorage', () => {
    const service = TestBed.inject(ModeService);
    service.setMode('Simple');
    expect(service.mode()).toBe('Simple');
    expect(service.isSimple()).toBe(true);
    expect(localStorage.getItem('rpa-studio-mode')).toBe('Simple');
  });

  it('toggle flips between Simple and Advanced', () => {
    const service = TestBed.inject(ModeService);
    expect(service.mode()).toBe('Advanced');
    service.toggle();
    expect(service.mode()).toBe('Simple');
    service.toggle();
    expect(service.mode()).toBe('Advanced');
  });

  it('reads a persisted mode preference on construction', () => {
    localStorage.setItem('rpa-studio-mode', 'Simple');
    const service = TestBed.inject(ModeService);
    expect(service.mode()).toBe('Simple');
  });
});
