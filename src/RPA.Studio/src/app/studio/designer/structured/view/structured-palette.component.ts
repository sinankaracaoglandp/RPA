import {
  ChangeDetectionStrategy, ChangeDetectorRef, Component, EventEmitter, OnInit, Output, inject, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ActivityCatalogService } from '../../../../shared/services/activity-catalog.service';
import { TranslationService } from '../../../../core/translation.service';
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
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly i18n = inject(TranslationService);
  readonly filter = inject(StructuredPaletteFilter);

  /** Palet katlanabilir — tuvale yer açmak için (yalnız görünüm durumu, kalıcı değil). */
  readonly collapsed = signal(true);
  readonly search = signal('');

  toggleCollapsed(): void { this.collapsed.update((c) => !c); }

  onSearch(term: string): void { this.search.set(term); }

  /** Çip adı arama terimini içeriyor mu (kontrol çiplerinde çevrilmiş ad üzerinden). */
  private matches(label: string): boolean {
    const term = this.search().trim().toLocaleLowerCase('tr');
    return !term || label.toLocaleLowerCase('tr').includes(term);
  }

  private readonly controlTypes: ContainerType[] = ['if', 'forEach', 'for', 'while', 'tryCatch'];

  readonly controlChips: ControlChip[] = this.controlTypes.map((type) => ({
    type, label: 'structured.type.' + type, category: CONTROL_CATEGORY, factory: () => newContainer(type),
  }));
  activityChips: Chip[] = [];

  /**
   * Sürüklemeden ekleme: çipe çift tık ya da çipin `+` düğmesi. Palet yalnız "hangi node"
   * bilgisini yayınlar; yerleştirme mantığı StructuredViewComponent'e aittir (kural C).
   */
  @Output() readonly add = new EventEmitter<StructuredItem>();

  emitAdd(chip: Chip): void {
    this.add.emit(chip.factory());
  }

  /** `+` düğmesine basmak cdkDrag'i başlatmamalı (aksi halde tıklama kaybolur). */
  onAddPointerDown(event: Event): void {
    event.stopPropagation();
  }

  ngOnInit(): void {
    // Katalog yanıtı OnPush görünümünü kendiliğinden kirletmez — markForCheck olmadan
    // aktivite çipleri hiç render edilmez (yalnız başka bir olay CD tetiklerse görünürler).
    this.catalog.getActivities().subscribe({
      next: (list) => {
        this.activityChips = list
          .filter((a) => !CONTROL_ACTIVITY_IDS.has(a.activityId))
          .map((a) => ({
            label: a.displayName || a.activityId,
            category: a.category || 'Diğer',
            factory: () => newStep(a.activityId),
          }));
        this.cdr.markForCheck();
      },
      error: () => {
        this.activityChips = [];
        this.cdr.markForCheck();
      },
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
    const visible = s === null || s === CONTROL_CATEGORY ? this.controlChips : [];
    return visible.filter((c) => this.matches(this.i18n.translate(c.label)));
  }

  /** Seçili filtreye göre görünür aktivite çipleri. */
  get visibleActivityChips(): Chip[] {
    const s = this.selected;
    if (s === CONTROL_CATEGORY) { return []; }
    const byCat = s === null ? this.activityChips : this.activityChips.filter((c) => c.category === s);
    return byCat.filter((c) => this.matches(c.label));
  }
}
