import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { buildPatternFromSelection, explainRegex, REGEX_PRESETS, RegexPresetChip } from './regex-wizard.model';

@Component({
  selector: 'app-regex-wizard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './regex-wizard.component.html',
  styleUrls: ['./regex-wizard.component.scss'],
})
export class RegexWizardComponent {
  @Input() sampleText = '';
  @Output() readonly patternApply = new EventEmitter<{ pattern: string; group: string }>();

  readonly presets = REGEX_PRESETS;
  pattern = '';
  group = 'deger';
  error = '';

  usePreset(preset: RegexPresetChip): void {
    this.pattern = preset.pattern;
    this.group = preset.group;
    this.error = '';
  }

  generateFromSelection(textarea: HTMLTextAreaElement): void {
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    if (start === end) { this.error = 'Önce örnek metinde çıkarmak istediğin değeri seç.'; return; }
    const built = buildPatternFromSelection(textarea.value, start, end);
    this.pattern = built.pattern;
    this.group = built.group;
    this.error = '';
  }

  explanation(): string {
    return this.pattern ? explainRegex(this.pattern) : '';
  }

  /** Örnek metin üzerinde canlı deneme; hatalı desen kullanıcıya nazikçe bildirilir. */
  liveMatch(): string {
    if (!this.pattern || !this.sampleText) return '';
    try {
      const match = new RegExp(this.pattern).exec(this.sampleText);
      if (!match) return 'Örnek metinde eşleşme yok.';
      const value = this.group ? match.groups?.[this.group] ?? match[0] : match[0];
      return `Bulunan: ${value}`;
    } catch {
      return 'Desen geçersiz.';
    }
  }

  apply(): void {
    if (!this.pattern) { this.error = 'Önce bir desen seç veya üret.'; return; }
    this.patternApply.emit({ pattern: this.pattern, group: this.group });
  }
}
