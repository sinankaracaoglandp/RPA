import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { WorkflowVariable } from '../../../shared/models/workflow.model';

const VARIABLE_TYPES = ['string', 'int', 'decimal', 'bool', 'DateTime', 'JSON'] as const;

@Component({
  selector: 'app-variables-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './variables-panel.component.html',
  styleUrls: ['./variables-panel.component.scss'],
})
export class VariablesPanelComponent {
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly variablesChange = new EventEmitter<WorkflowVariable[]>();

  readonly variableTypes = VARIABLE_TYPES;

  addVariable(): void {
    this.emit([
      ...this.variables,
      { name: this.nextVariableName(), type: 'string', scope: 'global', default: '' },
    ]);
  }

  updateVariable(index: number, patch: Partial<WorkflowVariable>): void {
    const next = this.variables.map((variable, i) =>
      i === index ? this.normalizeVariable({ ...variable, ...patch }) : variable,
    );
    this.emit(next);
  }

  removeVariable(index: number): void {
    this.emit(this.variables.filter((_, i) => i !== index));
  }

  defaultValue(variable: WorkflowVariable): string {
    if (variable.default === null || variable.default === undefined) {
      return '';
    }
    if ((variable.type ?? '').toLowerCase() === 'datetime') {
      return this.formatDateTimeInput(variable.default);
    }
    return typeof variable.default === 'object' ? JSON.stringify(variable.default) : String(variable.default);
  }

  defaultInputType(variable: WorkflowVariable): 'text' | 'number' | 'datetime-local' {
    switch ((variable.type ?? '').toLowerCase()) {
      case 'int':
      case 'number':
      case 'decimal':
        return 'number';
      case 'datetime':
        return 'datetime-local';
      default:
        return 'text';
    }
  }

  onDefaultChange(index: number, raw: string): void {
    const variable = this.variables[index];
    this.updateVariable(index, { default: this.parseDefault(variable?.type ?? 'string', raw) });
  }

  trackByIndex(index: number): number {
    return index;
  }

  variablePathsFor(rootPath: string): string[] {
    const [rootName, ...segments] = rootPath.split('.').filter(Boolean);
    const variable = this.variables.find((item) => item.name === rootName);
    if (!variable?.schema || typeof variable.schema !== 'object') {
      return [];
    }
    const schema = this.schemaAt(variable.schema as JsonSchemaLike, segments);
    if (!schema) {
      return [];
    }
    if (schema.type === 'array' && schema.items) {
      return this.propertyNames(schema.items).map((name) => `satir.${name}`);
    }
    return this.propertyNames(schema).map((name) => `${rootPath}.${name}`);
  }

  private emit(next: WorkflowVariable[]): void {
    this.variables = next;
    this.variablesChange.emit(next);
  }

  private nextVariableName(): string {
    const used = new Set(this.variables.map((v) => v.name));
    let index = this.variables.length + 1;
    let candidate = `degisken${index}`;
    while (used.has(candidate)) {
      index++;
      candidate = `degisken${index}`;
    }
    return candidate;
  }

  private normalizeVariable(variable: WorkflowVariable): WorkflowVariable {
    const type = variable.type || 'string';
    return {
      name: variable.name,
      type,
      scope: variable.scope ?? 'global',
      default: this.parseDefault(type, this.defaultValue(variable)),
    };
  }

  private parseDefault(type: string, raw: string): unknown {
    switch (type.toLowerCase()) {
      case 'int':
      case 'number':
      case 'decimal':
        return raw === '' ? null : Number(raw);
      case 'bool':
      case 'boolean':
        return raw === 'true' || raw === '1';
      case 'datetime':
        if (!raw.trim()) {
          return null;
        }
        return this.normalizeDateTime(raw);
      case 'json':
        if (!raw.trim()) {
          return null;
        }
        try {
          return JSON.parse(raw);
        } catch {
          return raw;
        }
      default:
        return raw;
    }
  }

  private normalizeDateTime(raw: string): string {
    // datetime-local girişi (tz'siz) UTC olarak yorumlanır — böylece saklanan ISO değer,
    // makinenin saat diliminden bağımsızdır (TZ-stabil round-trip).
    const hasTimezone = /Z|[+-]\d{2}:\d{2}$/.test(raw);
    const parsed = new Date(hasTimezone ? raw : `${raw}Z`);
    return Number.isNaN(parsed.getTime()) ? raw : parsed.toISOString();
  }

  private formatDateTimeInput(value: unknown): string {
    const parsed = value instanceof Date ? value : new Date(String(value));
    if (Number.isNaN(parsed.getTime())) {
      return String(value);
    }

    // UTC bileşenleriyle biçimlendir (yerel değil) — ekranda saklanan UTC saati birebir gösterilir.
    const pad = (part: number) => String(part).padStart(2, '0');
    return [
      parsed.getUTCFullYear(),
      pad(parsed.getUTCMonth() + 1),
      pad(parsed.getUTCDate()),
    ].join('-') + `T${pad(parsed.getUTCHours())}:${pad(parsed.getUTCMinutes())}`;
  }

  private schemaAt(schema: JsonSchemaLike, segments: string[]): JsonSchemaLike | null {
    let current: JsonSchemaLike | null = schema;
    for (const segment of segments) {
      if (!current) return null;
      if (current.type === 'array') current = current.items ?? null;
      current = current?.properties?.[segment] ?? null;
    }
    return current;
  }

  private propertyNames(schema: JsonSchemaLike): string[] {
    const target = schema.type === 'array' ? schema.items : schema;
    return Object.keys(target?.properties ?? {});
  }
}

interface JsonSchemaLike {
  type?: string;
  properties?: Record<string, JsonSchemaLike>;
  items?: JsonSchemaLike;
}
