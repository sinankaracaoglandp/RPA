import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { TemplateMetadata } from '../../shared/models/template.model';

/** A single template preview card (name, description, icon, category). */
@Component({
  selector: 'app-template-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './template-card.component.html',
  styleUrls: ['./template-card.component.scss'],
})
export class TemplateCardComponent {
  @Input({ required: true }) template!: TemplateMetadata;
  @Output() readonly select = new EventEmitter<TemplateMetadata>();

  onSelect(): void {
    this.select.emit(this.template);
  }
}
