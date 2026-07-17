import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  explainRegex,
  findValue,
  REGEX_PRESETS,
  RegexPresetChip,
  TextScope,
  ValueMatch,
} from './regex-wizard.model';

/**
 * Değer bulucu: kullanıcı aradığı değeri yazar; örnek XML'de nerede geçtiği
 * vurgulanır ve o değeri yakalayan regex üretilir. Regex bilgisi gerekmez.
 */
@Component({
  selector: 'app-regex-wizard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './regex-wizard.component.html',
  styleUrls: ['./regex-wizard.component.scss'],
})
export class RegexWizardComponent {
  /** Örnek XML'deki (yol, metin) çiftleri; arama bunların içinde yapılır. */
  @Input() scopes: TextScope[] = [];
  /** Regex desenini alana yaz. */
  @Output() readonly patternApply = new EventEmitter<{ pattern: string; group: string }>();
  /** Değer bir XML öğesinde bulunduysa doğrudan o yolu kullan (regex'e gerek yok). */
  @Output() readonly pathApply = new EventEmitter<string>();

  readonly presets = REGEX_PRESETS;
  searchValue = '';
  presetsOpen = false;

  matches(): ValueMatch[] {
    return findValue(this.scopes, this.searchValue);
  }

  get searched(): boolean {
    return this.searchValue.trim().length > 0;
  }

  explain(pattern: string): string {
    return explainRegex(pattern);
  }

  usePath(match: ValueMatch): void {
    this.pathApply.emit(match.path);
  }

  usePattern(match: ValueMatch): void {
    this.patternApply.emit({ pattern: match.pattern, group: match.group });
  }

  usePreset(preset: RegexPresetChip): void {
    this.patternApply.emit({ pattern: preset.pattern, group: preset.group });
  }
}
