import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';
import { SpyElement, SpyKind, SpyService } from '../../../shared/services/spy.service';

@Component({
  selector: 'app-selector-picker-button',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './selector-picker-button.component.html',
  styleUrls: ['./selector-picker-button.component.scss'],
})
export class SelectorPickerButtonComponent {
  @Input() pickerKind?: SpyKind | null;
  @Output() readonly picked = new EventEmitter<SpyElement>();

  readonly state = signal<'idle' | 'picking' | 'error'>('idle');

  constructor(private readonly spy: SpyService) {}

  async pick(): Promise<void> {
    if (!this.pickerKind || this.state() === 'picking') {
      return;
    }

    this.state.set('picking');
    try {
      const element = await this.spy.pick(this.pickerKind);
      this.picked.emit(element);
      this.state.set('idle');
    } catch {
      this.state.set('error');
    }
  }
}
