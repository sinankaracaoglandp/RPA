import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../shared/services/activity-catalog.service';
import { ActivityMetadata } from '../../../shared/models/activity.model';
import { ActivityItemComponent } from './activity-item.component';
import { CanvasComponent } from '../canvas/canvas.component';

/** Sentinel category value meaning "no filter". */
const ALL_CATEGORIES = '__all__';

/**
 * Left-hand activity catalog panel. Lets the designer search/filter the
 * activity catalog and drop (or click-to-add) activities onto the canvas.
 */
@Component({
  selector: 'app-toolbox',
  standalone: true,
  imports: [CommonModule, TranslatePipe, ActivityItemComponent, CdkDropList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toolbox.component.html',
  styleUrls: ['./toolbox.component.scss'],
})
export class ToolboxComponent {
  /** Canvas to add activities to when dropped/activated. */
  @Input() canvas?: CanvasComponent;

  @Output() readonly activityAdded = new EventEmitter<{
    activityId: string;
    position?: { x: number; y: number };
  }>();

  private readonly catalog = inject(ActivityCatalogService);

  readonly ALL_CATEGORIES = ALL_CATEGORIES;

  readonly activities = signal<ActivityMetadata[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly searchTerm = signal('');
  readonly selectedCategory = signal<string>(ALL_CATEGORIES);

  readonly categories = computed(() => {
    const set = new Set<string>();
    for (const activity of this.activities()) {
      if (activity.category) {
        set.add(activity.category);
      }
    }
    return Array.from(set).sort();
  });

  readonly filteredActivities = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const category = this.selectedCategory();
    return this.activities().filter((activity) => {
      const matchesCategory = category === ALL_CATEGORIES || activity.category === category;
      const matchesTerm = !term || activity.displayName.toLowerCase().includes(term);
      return matchesCategory && matchesTerm;
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.catalog.getActivities().subscribe({
      next: (activities) => {
        this.activities.set(activities ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  onSearchChange(term: string): void {
    this.searchTerm.set(term);
  }

  selectCategory(category: string): void {
    this.selectedCategory.set(category);
  }

  async addActivity(activityId: string, position?: { x: number; y: number }): Promise<void> {
    if (this.canvas) {
      const meta = this.activities().find((a) => a.activityId === activityId);
      await this.canvas.addNode(activityId, {
        ...(position ? { position } : {}),
        ...(meta?.displayName ? { label: meta.displayName } : {}),
        ...(meta?.defaultProperties ? { properties: { ...meta.defaultProperties } } : {}),
      });
    }
    this.activityAdded.emit({ activityId, position });
  }

  onActivityDragEnded(event: { activityId: string; clientX: number; clientY: number }): void {
    if (!this.canvas?.containsClientPoint(event.clientX, event.clientY)) {
      return;
    }

    void this.addActivity(
      event.activityId,
      this.canvas.clientToCanvasPosition(event.clientX, event.clientY),
    );
  }

  trackByActivityId(_index: number, activity: ActivityMetadata): string {
    return activity.activityId;
  }
}
