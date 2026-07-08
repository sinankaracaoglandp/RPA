import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { WorkflowVariable } from '../../../shared/models/workflow.model';

const VARIABLE_TYPES = ['string', 'int', 'decimal', 'bool', 'JSON'] as const;

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
    return typeof variable.default === 'object' ? JSON.stringify(variable.default) : String(variable.default);
  }

  onDefaultChange(index: number, raw: string): void {
    const variable = this.variables[index];
    this.updateVariable(index, { default: this.parseDefault(variable?.type ?? 'string', raw) });
  }

  trackByIndex(index: number): number {
    return index;
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
}
