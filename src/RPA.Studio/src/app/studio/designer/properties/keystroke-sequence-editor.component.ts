import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';

export type KeystrokeStepType = 'chord' | 'text';

/** Desktop.SendKeys tuş dizisi adımı: ya bir tuş vuruşu (chord) ya da düz metin. */
export interface KeystrokeStep {
  type: KeystrokeStepType;
  modifiers: string[];
  key: string;
  text: string;
  waitMs: number;
}

/** Kanonik modifier sırası ve görünen etiketleri (canlı önizleme için). */
const MODIFIER_ORDER = ['ctrl', 'shift', 'alt', 'altgr', 'win'];
const MODIFIER_LABELS: Record<string, string> = {
  ctrl: 'Ctrl',
  shift: 'Shift',
  alt: 'Alt',
  altgr: 'AltGr',
  win: 'Win',
};

function range(from: number, to: number): string[] {
  const out: string[] = [];
  for (let i = from; i <= to; i++) {
    out.push(String(i));
  }
  return out;
}

const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('');
const FUNCTION_KEYS = Array.from({ length: 12 }, (_, i) => `F${i + 1}`);
const NAV_KEYS = [
  'Home', 'End', 'PageUp', 'PageDown',
  'Up', 'Down', 'Left', 'Right',
  'Tab', 'Enter', 'Esc', 'Space', 'Backspace', 'Delete', 'Insert',
];

/**
 * Desktop.SendKeys için yapısal tuş dizisi editörü (vision-sequence deseni). İstenildiği kadar
 * adım eklenebilir; her adım ya bir tuş vuruşu (modifier checkbox'ları + ana tuş dropdown'u,
 * örn. Ctrl+A, F4, Home) ya da düz metindir. Değer, backend'in beklediği JSON adım dizisi olarak
 * `valueChange` ile emit edilir. Eski düz-metin değer tek bir metin adımı olarak yüklenir.
 */
@Component({
  selector: 'app-keystroke-sequence-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './keystroke-sequence-editor.component.html',
  styleUrls: ['./keystroke-sequence-editor.component.scss'],
})
export class KeystrokeSequenceEditorComponent {
  steps: KeystrokeStep[] = [];

  readonly modifierKeys = MODIFIER_ORDER;
  readonly modifierLabels = MODIFIER_LABELS;
  readonly keyGroups = [
    { labelKey: 'keystroke.keyGroupLetters', keys: [...LETTERS, ...range(0, 9)] },
    { labelKey: 'keystroke.keyGroupFunction', keys: FUNCTION_KEYS },
    { labelKey: 'keystroke.keyGroupNavigation', keys: NAV_KEYS },
  ];

  @Input()
  set value(v: unknown) {
    this.steps = this.parse(typeof v === 'string' ? v : '');
  }

  @Output() readonly valueChange = new EventEmitter<string>();

  addStep(): void {
    this.steps = [...this.steps, { type: 'chord', modifiers: [], key: 'A', text: '', waitMs: 0 }];
    this.emit();
  }

  removeStep(index: number): void {
    this.steps = this.steps.filter((_, i) => i !== index);
    this.emit();
  }

  moveUp(index: number): void {
    if (index <= 0) {
      return;
    }
    const next = [...this.steps];
    [next[index - 1], next[index]] = [next[index], next[index - 1]];
    this.steps = next;
    this.emit();
  }

  moveDown(index: number): void {
    if (index >= this.steps.length - 1) {
      return;
    }
    const next = [...this.steps];
    [next[index], next[index + 1]] = [next[index + 1], next[index]];
    this.steps = next;
    this.emit();
  }

  setType(index: number, type: KeystrokeStepType): void {
    this.steps[index] = { ...this.steps[index], type };
    this.emit();
  }

  toggleModifier(index: number, modifier: string): void {
    const step = this.steps[index];
    const has = step.modifiers.includes(modifier);
    const modifiers = has
      ? step.modifiers.filter((m) => m !== modifier)
      : [...step.modifiers, modifier];
    modifiers.sort((a, b) => MODIFIER_ORDER.indexOf(a) - MODIFIER_ORDER.indexOf(b));
    this.steps[index] = { ...step, modifiers };
    this.emit();
  }

  hasModifier(step: KeystrokeStep, modifier: string): boolean {
    return step.modifiers.includes(modifier);
  }

  setKey(index: number, key: string): void {
    this.steps[index] = { ...this.steps[index], key };
    this.emit();
  }

  setText(index: number, text: string): void {
    this.steps[index] = { ...this.steps[index], text };
    this.emit();
  }

  setWait(index: number, value: string | number): void {
    const n = Math.round(Number(value));
    this.steps[index] = { ...this.steps[index], waitMs: Number.isFinite(n) && n > 0 ? n : 0 };
    this.emit();
  }

  previewOf(step: KeystrokeStep): string {
    if (step.type === 'text') {
      return step.text;
    }
    const parts = step.modifiers.map((m) => MODIFIER_LABELS[m] ?? m);
    if (step.key) {
      parts.push(step.key);
    }
    return parts.join(' + ');
  }

  private parse(raw: string): KeystrokeStep[] {
    if (!raw || raw.trim().length === 0) {
      return [];
    }
    if (raw.trim().startsWith('[')) {
      try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) {
          return parsed.map((s: Record<string, unknown>) => this.normalize(s));
        }
      } catch {
        // düz metne düş
      }
    }
    return [{ type: 'text', modifiers: [], key: '', text: raw, waitMs: 0 }];
  }

  private normalize(s: Record<string, unknown>): KeystrokeStep {
    const type: KeystrokeStepType = s['type'] === 'text' ? 'text' : 'chord';
    const modifiers = Array.isArray(s['modifiers'])
      ? (s['modifiers'] as unknown[]).filter((m): m is string => typeof m === 'string')
      : [];
    return {
      type,
      modifiers,
      key: typeof s['key'] === 'string' ? (s['key'] as string) : '',
      text: typeof s['text'] === 'string' ? (s['text'] as string) : '',
      waitMs: typeof s['waitMs'] === 'number' ? (s['waitMs'] as number) : 0,
    };
  }

  emit(): void {
    const payload = this.steps.map((s) =>
      s.type === 'text'
        ? { type: 'text', text: s.text, waitMs: s.waitMs }
        : { type: 'chord', modifiers: s.modifiers, key: s.key, waitMs: s.waitMs },
    );
    this.valueChange.emit(JSON.stringify(payload));
  }
}
