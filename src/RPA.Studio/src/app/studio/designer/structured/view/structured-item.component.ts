import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList, CdkDragDrop } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ContainerItem, ContainerType, LaneName, StructuredItem, StructuredSequence, lanesFor } from '../structured-model';
import { StructuredAddMenuComponent } from './structured-add-menu.component';

/** Yapısal editör düzenleme olayı; öğe referansı taşır (path `findPath` ile host'ta çıkar). */
export type StructuredAction =
  | { kind: 'delete' | 'up' | 'down'; target: StructuredItem }
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
  /** Dizideki konum — akış bağlacı (üst) ve giriş/çıkış portlarının gösterimini belirler. */
  @Input() first = false;
  @Input() last = false;
  @Output() readonly action = new EventEmitter<StructuredAction>();
  @Output() readonly drop = new EventEmitter<CdkDragDrop<StructuredSequence>>();
  @Output() readonly select = new EventEmitter<StructuredItem>();

  get isSelected(): boolean { return this.item === this.selectedRef; }

  onSelect(event: Event): void {
    event.stopPropagation();
    this.select.emit(this.item);
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
    return this.item.node.activity ?? this.item.node.type;
  }

  emitAction(kind: 'delete' | 'up' | 'down', event?: Event): void {
    event?.stopPropagation();
    this.action.emit({ kind, target: this.item });
  }

  onLaneAdd(container: ContainerItem, lane: LaneName, item: StructuredItem): void {
    this.action.emit({ kind: 'add', container, lane, item });
  }
}
