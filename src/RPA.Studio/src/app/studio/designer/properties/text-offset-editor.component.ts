import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { ImagePickerOptions, SpyElement, SpyService } from '../../../shared/services/spy.service';

/**
 * Vision.ClickTextOffset için çapa+ofset editörü. 🎯 ile iki aşamalı picker'ı (text-offset) çağırır:
 * çapa metni + hedef nokta seçilir, dx/dy otomatik hesaplanır. Alanlar elle de düzeltilebilir.
 * Değer backend'e JSON {anchorText,dx,dy} olarak verilir.
 */
@Component({
  selector: 'app-text-offset-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './text-offset-editor.component.html',
  styleUrls: ['./text-offset-editor.component.scss'],
})
export class TextOffsetEditorComponent {
  // Test'lerde bileşen `new` ile (injection context dışında) oluşturulur; bu durumda inject()
  // NG0203 fırlatır — pick() zaten yalnız çalışan Studio'da (TestBed/gerçek DI) çağrılır.
  private readonly spy: SpyService | undefined = (() => {
    try {
      return inject(SpyService, { optional: true }) ?? undefined;
    } catch {
      return undefined;
    }
  })();

  anchorText = '';
  dx = 0;
  dy = 0;
  preview: string | null = null;
  picking = false;
  error: string | null = null;

  @Input()
  set value(v: unknown) {
    const parsed = this.parse(typeof v === 'string' ? v : '');
    this.anchorText = parsed.anchorText;
    this.dx = parsed.dx;
    this.dy = parsed.dy;
  }

  @Output() readonly valueChange = new EventEmitter<string>();

  async pick(): Promise<void> {
    if (!this.spy || this.picking) {
      return;
    }
    this.picking = true;
    this.error = null;
    const options: ImagePickerOptions = { captureMode: 'f2', delaySeconds: 5 };
    try {
      const element = await this.spy.pick('text-offset', options);
      this.onPicked(element);
    } catch (e) {
      this.error = e instanceof Error ? e.message : String(e);
    } finally {
      this.picking = false;
    }
  }

  onPicked(element: SpyElement): void {
    if (element.kind !== 'text-offset') {
      return;
    }
    this.anchorText = element.anchorText ?? '';
    this.dx = element.dx ?? 0;
    this.dy = element.dy ?? 0;
    this.preview = element.imageBase64 ?? null;
    this.emit();
  }

  emit(): void {
    this.valueChange.emit(JSON.stringify({ anchorText: this.anchorText, dx: Math.round(this.dx), dy: Math.round(this.dy) }));
  }

  private parse(json: string): { anchorText: string; dx: number; dy: number } {
    if (!json || json.trim().length === 0) {
      return { anchorText: '', dx: 0, dy: 0 };
    }
    try {
      const p = JSON.parse(json) as Record<string, unknown>;
      return {
        anchorText: typeof p['anchorText'] === 'string' ? (p['anchorText'] as string) : '',
        dx: typeof p['dx'] === 'number' ? (p['dx'] as number) : 0,
        dy: typeof p['dy'] === 'number' ? (p['dy'] as number) : 0,
      };
    } catch {
      return { anchorText: '', dx: 0, dy: 0 };
    }
  }
}
