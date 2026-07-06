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
import { TranslatePipe } from '../../core/translate.pipe';
import { ActivityCatalogService } from '../../shared/services/activity-catalog.service';
import { ActivityMetadata } from '../../shared/models/activity.model';
import { CanvasComponent } from '../designer/canvas/canvas.component';

/**
 * Fixed set of the ~10 most common activities shown in Simple mode
 * (Faz 5, Task 5.5). Simple mode hides the full 52-activity catalog to keep
 * the surface approachable for non-technical users.
 */
export const SIMPLE_ACTIVITY_IDS: readonly string[] = [
  'Sap.Gui.Connect',
  'Sap.Nco.CallBapi',
  'Web.Navigate',
  'Web.Click',
  'Mail.Send',
  'Mail.Read',
  'Excel.Read',
  'Excel.Write',
  'Api.Get',
  'File.Copy',
];

/**
 * Simple-mode activity list. Same data source as the full toolbox
 * (ActivityCatalogService) but filtered to a small, curated subset and with
 * no category filter / expression editing — click-to-add only.
 */
@Component({
  selector: 'app-simplified-toolbox',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './simplified-toolbox.component.html',
  styleUrls: ['./simplified-toolbox.component.scss'],
})
export class SimplifiedToolboxComponent {
  /** Canvas to add activities to when activated. */
  @Input() canvas?: CanvasComponent;

  @Output() readonly activityAdded = new EventEmitter<{ activityId: string }>();

  private readonly catalog = inject(ActivityCatalogService);

  readonly activities = signal<ActivityMetadata[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);

  readonly simplifiedActivities = computed(() =>
    this.activities().filter((a) => SIMPLE_ACTIVITY_IDS.includes(a.activityId)),
  );

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

  async addActivity(activityId: string): Promise<void> {
    if (this.canvas) {
      await this.canvas.addNode(activityId);
    }
    this.activityAdded.emit({ activityId });
  }

  trackByActivityId(_index: number, activity: ActivityMetadata): string {
    return activity.activityId;
  }
}
