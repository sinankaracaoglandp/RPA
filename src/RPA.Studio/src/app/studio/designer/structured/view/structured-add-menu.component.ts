import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../../shared/services/activity-catalog.service';
import { ActivityMetadata } from '../../../../shared/models/activity.model';
import { ContainerType, StructuredItem } from '../structured-model';
import { newContainer, newStep } from '../edit/tree-ops';
import { CONTROL_ACTIVITY_IDS } from '../edit/control-activity-map';

/**
 * Yapısal editörde bir diziye/lane'e yeni öğe eklemek için küçük menü:
 * kontrol tipleri (Eğer/Her Biri İçin/…) doğrudan + aktivite açılır listesi (katalogdan).
 */
@Component({
  selector: 'app-structured-add-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './structured-add-menu.component.html',
  styleUrls: ['./structured-add-menu.component.scss'],
})
export class StructuredAddMenuComponent implements OnInit {
  private readonly catalog = inject(ActivityCatalogService);

  @Input() open = false;
  @Output() readonly pick = new EventEmitter<StructuredItem>();

  readonly controlTypes: ContainerType[] = ['if', 'forEach', 'for', 'while', 'tryCatch'];
  activities: ActivityMetadata[] = [];

  ngOnInit(): void {
    this.catalog.getActivities().subscribe({
      // Kontrol-akışı aktiviteleri (Logic.If/ForEach/...) düz aktivite olarak eklenmemeli;
      // bunlar yalnız kontrol tipi düğmeleriyle (gerçek konteyner) eklenir.
      next: (a) => (this.activities = a.filter((x) => !CONTROL_ACTIVITY_IDS.has(x.activityId))),
      error: () => (this.activities = []),
    });
  }

  toggle(): void { this.open = !this.open; }

  chooseControl(type: ContainerType): void {
    this.pick.emit(newContainer(type));
    this.open = false;
  }

  chooseActivity(activityId: string): void {
    if (!activityId) { return; }
    this.pick.emit(newStep(activityId));
    this.open = false;
  }
}
