import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { SpyElement } from '../../../shared/services/spy.service';
import { SelectorPickerButtonComponent } from './selector-picker-button.component';

/** Vision.ClickSequence adımı: aranacak görüntü (base64 PNG), tıklama türü, sonraki bekleme (ms). */
interface SequenceStep {
  image: string;
  clickType: string;
  waitMs: number;
}

/**
 * Vision.ClickSequence için sıralı adım editörü. İstenildiği kadar adım eklenebilir (iç içe
 * menüler için); her adım 🎯 ile görüntü seçer, tıklama türü ve sonraki bekleme (ms) taşır.
 * Değer, backend'in beklediği JSON dizisi olarak `valueChange` ile dışarı verilir.
 */
@Component({
  selector: 'app-vision-sequence-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, SelectorPickerButtonComponent],
  templateUrl: './vision-sequence-editor.component.html',
  styleUrls: ['./vision-sequence-editor.component.scss'],
})
export class VisionSequenceEditorComponent {
  steps: SequenceStep[] = [];
  readonly clickTypes = ['left', 'right', 'double'];

  @Input()
  set value(v: unknown) {
    this.steps = this.parse(typeof v === 'string' ? v : '');
  }

  @Output() readonly valueChange = new EventEmitter<string>();

  addStep(): void {
    this.steps = [...this.steps, { image: '', clickType: 'left', waitMs: 0 }];
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

  onPicked(index: number, element: SpyElement): void {
    if (element.kind !== 'image') {
      return;
    }
    this.steps[index] = { ...this.steps[index], image: element.imageBase64 ?? '' };
    this.emit();
  }

  onClickType(index: number, value: string): void {
    this.steps[index] = { ...this.steps[index], clickType: value };
    this.emit();
  }

  onWait(index: number, value: string | number): void {
    const n = Math.round(Number(value));
    this.steps[index] = { ...this.steps[index], waitMs: Number.isFinite(n) && n > 0 ? n : 0 };
    this.emit();
  }

  previewOf(step: SequenceStep): string | null {
    return step.image && step.image.length > 0 ? step.image : null;
  }

  private parse(json: string): SequenceStep[] {
    if (!json || json.trim().length === 0) {
      return [];
    }
    try {
      const parsed = JSON.parse(json);
      if (!Array.isArray(parsed)) {
        return [];
      }
      return parsed.map((s: Record<string, unknown>) => ({
        image: typeof s['image'] === 'string' ? (s['image'] as string) : '',
        clickType: typeof s['clickType'] === 'string' ? (s['clickType'] as string) : 'left',
        waitMs: typeof s['waitMs'] === 'number' ? (s['waitMs'] as number) : 0,
      }));
    } catch {
      return [];
    }
  }

  private emit(): void {
    this.valueChange.emit(JSON.stringify(this.steps));
  }
}
