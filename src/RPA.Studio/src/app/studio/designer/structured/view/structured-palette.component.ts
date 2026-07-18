import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../../shared/services/activity-catalog.service';
import { ContainerType, StructuredItem } from '../structured-model';
import { newContainer, newStep } from '../edit/tree-ops';
import { CONTROL_ACTIVITY_IDS } from '../edit/control-activity-map';
import { CONTROL_CATEGORY, StructuredPaletteFilter } from './structured-palette-filter';

interface Chip { label: string; category: string; factory: () => StructuredItem; }
interface ControlChip extends Chip { type: ContainerType; }

/**
 * Yapısal editör sürükle paleti: kontrol tipleri + katalog aktiviteleri için `cdkDrag` çipleri,
 * kategoriye göre gruplanır. Bir kategori sekmesine tıklamak hem paleti hem ekleme menüsündeki
 * aktivite dropdown'ını o kategoriye filtreler (paylaşılan <see cref="StructuredPaletteFilter"/>).
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
  readonly filter = inject(StructuredPaletteFilter);

  private readonly controlTypes: ContainerType[] = ['if', 'forEach', 'for', 'while', 'tryCatch'];

  readonly controlChips: ControlChip[] = this.controlTypes.map((type) => ({
    type, label: 'structured.type.' + type, category: CONTROL_CATEGORY, factory: () => newContainer(type),
  }));
  activityChips: Chip[] = [];

  ngOnInit(): void {
    this.catalog.getActivities().subscribe({
      next: (list) => (this.activityChips = list
        .filter((a) => !CONTROL_ACTIVITY_IDS.has(a.activityId))
        .map((a) => ({
          label: a.displayName || a.activityId,
          category: a.category || 'Diğer',
          factory: () => newStep(a.activityId),
        }))),
      error: () => (this.activityChips = []),
    });
  }

  /** Sekme olarak gösterilecek kategoriler: 'Kontrol' + aktivite kategorileri (benzersiz, sıralı). */
  get categories(): string[] {
    const activityCats = [...new Set(this.activityChips.map((c) => c.category))].sort((a, b) => a.localeCompare(b, 'tr'));
    return [CONTROL_CATEGORY, ...activityCats];
  }

  get selected(): string | null {
    return this.filter.category();
  }

  isActive(category: string): boolean {
    return this.selected === category;
  }

  select(category: string): void {
    this.filter.toggle(category);
  }

  /** Seçili filtreye göre görünür kontrol çipleri (Kontrol seçili ya da filtre yokken). */
  get visibleControlChips(): ControlChip[] {
    const s = this.selected;
    return s === null || s === CONTROL_CATEGORY ? this.controlChips : [];
  }

  /** Seçili filtreye göre görünür aktivite çipleri. */
  get visibleActivityChips(): Chip[] {
    const s = this.selected;
    if (s === CONTROL_CATEGORY) { return []; }
    return s === null ? this.activityChips : this.activityChips.filter((c) => c.category === s);
  }
}
