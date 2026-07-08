import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivityCatalogService } from '../../../shared/services/activity-catalog.service';
import { ActivityMetadata, ActivityPort } from '../../../shared/models/activity.model';
import { WorkflowVariable } from '../../../shared/models/workflow.model';
import { SpyElement } from '../../../shared/services/spy.service';
import { SelectorPickerButtonComponent } from './selector-picker-button.component';

/**
 * Metadata güdümlü jenerik özellik editörü. Seçili aktivitenin katalog
 * metadata'sındaki `inputs` listesinden her parametre için bir form alanı üretir.
 * Web.* dışındaki tüm aktivite aileleri (SAP, Excel, Email, File, API ...) için
 * özel editör gerekmeden alan girişi sağlar (Faz 5 properties fallback).
 */
@Component({
  selector: 'app-generic-property',
  standalone: true,
  imports: [CommonModule, FormsModule, SelectorPickerButtonComponent],
  templateUrl: './generic-property.component.html',
  styleUrls: ['./generic-property.component.scss'],
})
export class GenericPropertyComponent {
  private readonly catalog = inject(ActivityCatalogService);
  private readonly cdr = inject(ChangeDetectorRef);

  private _activityType?: string;
  metadata?: ActivityMetadata;
  loading = false;
  error = false;

  @Input()
  set activityType(value: string | undefined) {
    if (value === this._activityType) {
      return;
    }
    this._activityType = value;
    this.loadMetadata(value);
  }
  get activityType(): string | undefined {
    return this._activityType;
  }

  @Input() properties: Record<string, unknown> = {};
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<Record<string, unknown>>();

  get inputs(): ActivityPort[] {
    return this.metadata?.inputs ?? [];
  }

  /** Alan tipini HTML input türüne eşler. */
  inputType(port: ActivityPort): 'text' | 'number' | 'checkbox' | 'password' {
    switch ((port.type ?? '').toLowerCase()) {
      case 'int':
      case 'number':
      case 'decimal':
        return 'number';
      case 'bool':
      case 'boolean':
        return 'checkbox';
      case 'credential':
        return 'password';
      default:
        return 'text';
    }
  }

  value(port: ActivityPort): unknown {
    return this.properties[port.name] ?? '';
  }

  boolValue(port: ActivityPort): boolean {
    return this.properties[port.name] === true;
  }

  isVariableField(port: ActivityPort): boolean {
    return ['variableName', 'outputVariable'].includes(port.name);
  }

  onValueChange(port: ActivityPort, raw: unknown): void {
    const next = { ...this.properties };
    if (this.inputType(port) === 'number') {
      const num = raw === '' || raw === null ? null : Number(raw);
      next[port.name] = num;
    } else {
      next[port.name] = raw;
    }
    this.properties = next;
    this.propertiesChange.emit(next);
  }

  onPicked(port: ActivityPort, element: SpyElement): void {
    this.onValueChange(port, element.elementId);
  }

  private loadMetadata(activityType: string | undefined): void {
    this.metadata = undefined;
    this.error = false;
    if (!activityType) {
      return;
    }
    this.loading = true;
    this.catalog.getActivity(activityType).subscribe({
      next: (meta) => {
        this.metadata = meta;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.error = true;
        this.loading = false;
        this.cdr.markForCheck();
      },
    });
  }
}
