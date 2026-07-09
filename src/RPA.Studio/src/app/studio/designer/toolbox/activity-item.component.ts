import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CdkDrag, CdkDragEnd, CdkDragStart } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../core/translate.pipe';
import { ActivityMetadata } from '../../../shared/models/activity.model';

/** Category → icon glyph fallback when the catalog does not supply one. */
const CATEGORY_ICONS: Record<string, string> = {
  Logic: '⚙',
  'SAP.GUI': '🖥',
  'SAP.Nco': '🔌',
  Web: '🌐',
  OTP: '🔐',
  Mail: '✉',
  File: '📁',
  Excel: '📊',
};
const DEFAULT_ICON = '▫';

/**
 * A single activity card in the toolbox list. Draggable onto the canvas via
 * Angular CDK; also addable via keyboard (Enter/Space) for accessibility.
 */
@Component({
  selector: 'app-activity-item',
  standalone: true,
  imports: [CommonModule, TranslatePipe, CdkDrag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './activity-item.component.html',
  styleUrls: ['./activity-item.component.scss'],
})
export class ActivityItemComponent {
  @Input({ required: true }) activity!: ActivityMetadata;

  @Output() readonly activate = new EventEmitter<string>();
  @Output() readonly dragStarted = new EventEmitter<string>();
  @Output() readonly dragEnded = new EventEmitter<{ activityId: string; clientX: number; clientY: number }>();

  get icon(): string {
    return this.activity.icon ?? CATEGORY_ICONS[this.activity.category ?? ''] ?? DEFAULT_ICON;
  }

  onActivate(): void {
    this.activate.emit(this.activity.activityId);
  }

  onDragStarted(_event: CdkDragStart): void {
    this.dragStarted.emit(this.activity.activityId);
  }

  onDragEnded(event: CdkDragEnd): void {
    this.dragEnded.emit({
      activityId: this.activity.activityId,
      clientX: event.dropPoint.x,
      clientY: event.dropPoint.y,
    });
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.onActivate();
    }
  }
}
