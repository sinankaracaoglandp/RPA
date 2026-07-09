import { CommonModule } from '@angular/common';
import { Component, EventEmitter, HostListener, Input, Output } from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';
import { WorkflowVariable } from '../../../shared/models/workflow.model';
import { WebPropertyRouterComponent, isWebActivityType } from './web-property-router.component';
import { GenericPropertyComponent } from './generic-property.component';

/**
 * Properties panel shown alongside the canvas (Faz 5 Task 5.6). Seçili node'un
 * aktivite tipi ve özellikleri designer tarafından DÜZ VERİ olarak verilir —
 * panel canvas'a referans tutmaz (Paket A: ViewChild bağlama bug'ı düzeltmesi).
 */
@Component({
  selector: 'app-properties-panel',
  standalone: true,
  imports: [CommonModule, TranslatePipe, WebPropertyRouterComponent, GenericPropertyComponent],
  templateUrl: './properties-panel.component.html',
  styleUrls: ['./properties-panel.component.scss'],
})
export class PropertiesPanelComponent {
  @Input() activityType?: string;
  @Input() properties: Record<string, unknown> = {};
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<Record<string, unknown>>();

  panelWidth = 360;
  private resizing = false;
  private resizeStartX = 0;
  private resizeStartWidth = 360;

  get isWebActivity(): boolean {
    return isWebActivityType(this.activityType);
  }

  onPropertiesChange(value: Record<string, unknown>): void {
    this.propertiesChange.emit(value);
  }

  startResize(event: PointerEvent): void {
    event.preventDefault();
    this.resizing = true;
    this.resizeStartX = event.clientX;
    this.resizeStartWidth = this.panelWidth;
  }

  @HostListener('window:pointermove', ['$event'])
  onResizeMove(event: PointerEvent): void {
    if (!this.resizing) {
      return;
    }
    const delta = this.resizeStartX - event.clientX;
    this.panelWidth = Math.max(300, Math.min(560, this.resizeStartWidth + delta));
  }

  @HostListener('window:pointerup')
  stopResize(): void {
    this.resizing = false;
  }
}
