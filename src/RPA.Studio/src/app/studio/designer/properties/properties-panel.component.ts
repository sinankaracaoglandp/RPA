import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
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
})
export class PropertiesPanelComponent {
  @Input() activityType?: string;
  @Input() properties: Record<string, unknown> = {};
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<Record<string, unknown>>();

  get isWebActivity(): boolean {
    return isWebActivityType(this.activityType);
  }

  onPropertiesChange(value: Record<string, unknown>): void {
    this.propertiesChange.emit(value);
  }
}
