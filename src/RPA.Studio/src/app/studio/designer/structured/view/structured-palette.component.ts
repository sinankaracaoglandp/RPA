import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../../shared/services/activity-catalog.service';
import { ContainerType, StructuredItem } from '../structured-model';
import { newContainer, newStep } from '../edit/tree-ops';
import { CONTROL_ACTIVITY_IDS } from '../edit/control-activity-map';

interface Chip { label: string; factory: () => StructuredItem; }
interface ControlChip extends Chip { type: ContainerType; }

/**
 * Yapısal editör sürükle paleti: kontrol tipleri + katalog aktiviteleri için `cdkDrag` çipleri.
 * Her çip `cdkDragData = { factory }`; palet dizisi mutasyona uğramaz (bırakmada yeni öğe üretilir).
 */
@Component({
  selector: 'app-structured-palette',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, CdkDrag, CdkDropList],
  templateUrl: './structured-palette.component.html',
  styleUrls: ['./structured-palette.component.scss'],
})
export class StructuredPaletteComponent implements OnInit {
  private readonly catalog = inject(ActivityCatalogService);
  private readonly controlTypes: ContainerType[] = ['if', 'forEach', 'for', 'while', 'tryCatch'];

  readonly controlChips: ControlChip[] = this.controlTypes.map((type) => ({
    type, label: 'structured.type.' + type, factory: () => newContainer(type),
  }));
  activityChips: Chip[] = [];

  ngOnInit(): void {
    this.catalog.getActivities().subscribe({
      // Kontrol-akışı aktiviteleri paletten çıkarılır; döngü/if yalnız kontrol çipleriyle eklenir.
      next: (list) => (this.activityChips = list
        .filter((a) => !CONTROL_ACTIVITY_IDS.has(a.activityId))
        .map((a) => ({
          label: a.displayName || a.activityId, factory: () => newStep(a.activityId),
        }))),
      error: () => (this.activityChips = []),
    });
  }
}
