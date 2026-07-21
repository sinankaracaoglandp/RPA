import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
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
  host: { '[class.selector-picker-host--image]': 'showKeyControls' },
})
export class SelectorPickerButtonComponent implements OnInit {
  @Input() pickerKind?: SpyKind | null;
  @Output() readonly picked = new EventEmitter<SpyElement>();

  readonly state = signal<'idle' | 'picking' | 'error'>('idle');
  /** Son hatanın açıklaması — rozetin tooltip'inde gösterilir. */
  readonly errorMessage = signal<string>('');

  // Paket F: image picker "ekran dondurma" ayarları (yalnız pickerKind === 'image').
  readonly captureMode = signal<ImageCaptureMode>('f2');
  readonly delaySeconds = signal<number>(5);
  // Manuel modda dondurma kısayolu (node'da seçilir; hedef uygulamada boş bir tuş kullanılabilsin).
  // CapsLock SAP için önerilir: SAP'ta F1–F12'nin tamamı transaction kısayoludur, Caps Lock ise
  // hiçbir SAP fonksiyonunu tetiklemez.
  // Harf tuşları Ctrl/Shift/Alt ile birlikte kullanılır (örn. Ctrl+T) — SAP'ta F1–F12 doludur.
  readonly hotKeys = [
    'CapsLock',
    'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10', 'F11', 'F12',
    ...'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split(''),
  ];
  readonly hotKey = signal<string>('F2');
  readonly ctrl = signal<boolean>(false);
  readonly shift = signal<boolean>(false);
  readonly alt = signal<boolean>(false);

  constructor(private readonly spy: SpyService) {}

  get isImage(): boolean {
    return this.pickerKind === 'image';
  }

  /** SAP seçimi onay tuşuyla verilir (tıklama SAP'ta alanı/butonu tetiklerdi). */
  get isSap(): boolean {
    return this.pickerKind === 'sap';
  }

  /** Tuş/modifier kontrolleri: image (dondurma) ve sap (seçim onayı) için. */
  get showKeyControls(): boolean {
    return this.isImage || this.isSap;
  }

  /**
   * Mod seçimi hem image (dondurma) hem sap (onay) için gösterilir.
   * SAP'ta F1–F12 tuşlarının tamamı transaction kısayoludur; zamanlayıcı modu tuşa hiç
   * basmadan seçim yapmayı sağlar.
   */
  get showCaptureMode(): boolean {
    return this.isImage || this.isSap;
  }

  onModeChange(mode: string): void {
    this.captureMode.set(mode === 'timer' ? 'timer' : 'f2');
  }

  onDelayChange(value: string | number): void {
    const n = Math.round(Number(value));
    this.delaySeconds.set(Number.isFinite(n) ? Math.min(120, Math.max(1, n)) : 5);
  }

  ngOnInit(): void {
    // SAP'ta F1–F12'nin tamamı transaction kısayoludur → varsayılan Ctrl+T (kullanıcı değiştirebilir).
    if (this.isSap) {
      this.hotKey.set('T');
      this.ctrl.set(true);
    }
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
      const options = this.showKeyControls
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
    } catch (error) {
      // Hatayı yutmak teşhisi imkânsız kılıyordu: kullanıcı yalnız "Hedef seçilemedi" görüyor,
      // sebebi (bağlantı kurulamadı / oturum zaten aktif / hub reddi) hiçbir yere yazılmıyordu.
      const message = error instanceof Error ? error.message : String(error);
      this.errorMessage.set(message);
      console.error('[picker] Hedef seçilemedi:', message, error);
      this.state.set('error');
    }
  }
}
