import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';
import {
  ImageCaptureMode,
  SpyElement,
  SpyKind,
  SpyService,
} from '../../../shared/services/spy.service';

@Component({
  selector: 'app-selector-picker-button',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './selector-picker-button.component.html',
  styleUrls: ['./selector-picker-button.component.scss'],
})
export class SelectorPickerButtonComponent {
  @Input() pickerKind?: SpyKind | null;
  @Output() readonly picked = new EventEmitter<SpyElement>();

  readonly state = signal<'idle' | 'picking' | 'error'>('idle');

  // Paket F: image picker "ekran dondurma" ayarları (yalnız pickerKind === 'image').
  readonly captureMode = signal<ImageCaptureMode>('f2');
  readonly delaySeconds = signal<number>(5);
  // Manuel modda dondurma kısayolu (node'da seçilir; hedef uygulamada boş bir tuş kullanılabilsin).
  readonly hotKeys = ['F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10', 'F11', 'F12'];
  readonly hotKey = signal<string>('F2');
  readonly ctrl = signal<boolean>(false);
  readonly shift = signal<boolean>(false);
  readonly alt = signal<boolean>(false);

  constructor(private readonly spy: SpyService) {}

  get isImage(): boolean {
    return this.pickerKind === 'image';
  }

  onModeChange(mode: string): void {
    this.captureMode.set(mode === 'timer' ? 'timer' : 'f2');
  }

  onDelayChange(value: string | number): void {
    const n = Math.round(Number(value));
    this.delaySeconds.set(Number.isFinite(n) ? Math.min(120, Math.max(1, n)) : 5);
  }

  onHotKeyChange(key: string): void {
    this.hotKey.set(this.hotKeys.includes(key) ? key : 'F2');
  }

  async pick(): Promise<void> {
    if (!this.pickerKind || this.state() === 'picking') {
      return;
    }

    this.state.set('picking');
    try {
      const options = this.isImage
        ? {
            captureMode: this.captureMode(),
            delaySeconds: this.delaySeconds(),
            hotKey: this.hotKey(),
            ctrl: this.ctrl(),
            shift: this.shift(),
            alt: this.alt(),
          }
        : undefined;
      const element = await this.spy.pick(this.pickerKind, options);
      this.picked.emit(element);
      this.state.set('idle');
    } catch {
      this.state.set('error');
    }
  }
}
