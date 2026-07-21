import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, Output, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList, CdkDragDrop } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ContainerItem, ContainerType, LaneName, StructuredItem, StructuredSequence, lanesFor } from '../structured-model';
import { StructuredAddMenuComponent } from './structured-add-menu.component';

/** Yapısal editör düzenleme olayı; öğe referansı taşır (path `findPath` ile host'ta çıkar). */
export type StructuredAction =
  | { kind: 'delete' | 'up' | 'down' | 'duplicate'; target: StructuredItem }
  | { kind: 'rename'; target: StructuredItem; label: string }
  | { kind: 'add'; container: ContainerItem; lane: LaneName; item: StructuredItem };

/**
 * Yapısal ağaçtaki tek bir öğeyi (adım kartı ya da lane'li konteyner kutusu) render eder.
 * Konteyner lane'leri KENDİNE özyinelemeyle (`app-structured-item`) render edilir — böylece
 * `StructuredSequenceComponent` ile dairesel import kurulmaz. `editable` iken düzenleme
 * denetimleri gösterir ve olayları referansla yukarı yayar.
 */
@Component({
  selector: 'app-structured-item',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredAddMenuComponent, CdkDrag, CdkDropList],
  templateUrl: './structured-item.component.html',
  styleUrls: ['./structured-item.component.scss'],
})
export class StructuredItemComponent {
  @Input({ required: true }) item!: StructuredItem;
  @Input() editable = false;
  @Input() selectedRef: StructuredItem | null = null;
  /** Çoklu seçimdeki tüm öğeler (vurgu bunun üyeliğine bakar). */
  @Input() selectedRefs: readonly StructuredItem[] = [];
  /** Dizideki konum — akış bağlacı (üst) ve giriş/çıkış portlarının gösterimini belirler. */
  @Input() first = false;
  @Input() last = false;
  @Output() readonly action = new EventEmitter<StructuredAction>();
  @Output() readonly drop = new EventEmitter<CdkDragDrop<StructuredSequence>>();
  @Output() readonly select = new EventEmitter<{ item: StructuredItem; additive: boolean }>();
  @Input() selectedLaneRef: { container: ContainerItem; lane: LaneName } | null = null;
  @Output() readonly selectLane = new EventEmitter<{ container: ContainerItem; lane: LaneName }>();

  isLaneSelected(c: ContainerItem, lane: LaneName): boolean {
    return this.selectedLaneRef?.container === c && this.selectedLaneRef?.lane === lane;
  }

  /** Lane'in boş alanına tıklama; çocuk kartlar kendi tıklamalarını zaten durdurur. */
  onLaneClick(c: ContainerItem, lane: LaneName, event: Event): void {
    event.stopPropagation();
    this.selectLane.emit({ container: c, lane });
  }

  get isSelected(): boolean {
    return this.item === this.selectedRef || this.selectedRefs.includes(this.item);
  }

  /** Ctrl/Cmd basılıysa seçim eklemeli olur (çoklu seçim). */
  onSelect(event: Event): void {
    event.stopPropagation();
    const me = event as MouseEvent;
    this.select.emit({ item: this.item, additive: !!(me.ctrlKey || me.metaKey) });
  }

  get container(): ContainerItem | null {
    return this.item.kind === 'container' ? this.item : null;
  }

  lanes(c: ContainerItem): LaneName[] {
    return lanesFor(c.type);
  }

  /** Blok başlığındaki tip ikonu (mockup ile aynı görsel dil). */
  containerIcon(type: ContainerType): string {
    switch (type) {
      case 'forEach': return '🔁';
      case 'for': return '🔢';
      case 'while': return '🔄';
      case 'if': return '◆';
      case 'tryCatch': return '🛡️';
      default: return '▸';
    }
  }

  laneItems(c: ContainerItem, lane: LaneName): StructuredSequence {
    return c.lanes[lane] ?? [];
  }

  /** Konteyner başlığındaki kısa props özeti. */
  summary(c: ContainerItem): string {
    const p = c.props;
    switch (c.type) {
      case 'forEach': return String(p['items'] ?? '');
      case 'for': return `${p['start'] ?? ''}..${p['end'] ?? ''}`;
      case 'while':
      case 'if': return String(p['condition'] ?? '');
      case 'tryCatch': return String(p['exceptionVariable'] ?? '');
      default: return '';
    }
  }

  stepTitle(): string {
    if (this.item.kind !== 'step') { return ''; }
    return this.item.node.label?.trim() || (this.item.node.activity ?? this.item.node.type);
  }

  /** Öğenin kullanıcı adı (yoksa boş) — konteyner başlığında tip etiketinin yerini alır. */
  labelOf(item: StructuredItem = this.item): string {
    const raw = item.kind === 'step' ? item.node.label : item.props['label'];
    return typeof raw === 'string' ? raw.trim() : '';
  }

  // ---- Satır-içi yeniden adlandırma ----
  renaming = false;
  private readonly renameInput = viewChild<ElementRef<HTMLInputElement>>('renameInput');

  startRename(event?: Event): void {
    event?.stopPropagation();
    if (!this.editable) { return; }
    this.renaming = true;
    setTimeout(() => this.renameInput()?.nativeElement.select());
  }

  commitRename(value: string): void {
    this.renaming = false;
    if (value.trim() === this.labelOf()) { return; }
    this.action.emit({ kind: 'rename', target: this.item, label: value });
  }

  cancelRename(): void { this.renaming = false; }

  emitAction(kind: 'delete' | 'up' | 'down' | 'duplicate', event?: Event): void {
    event?.stopPropagation();
    this.action.emit({ kind, target: this.item });
  }

  onLaneAdd(container: ContainerItem, lane: LaneName, item: StructuredItem): void {
    this.action.emit({ kind: 'add', container, lane, item });
  }
}
