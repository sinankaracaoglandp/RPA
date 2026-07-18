import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { ContainerItem, LaneName, StructuredItem, StructuredSequence, lanesFor } from '../structured-model';

/**
 * Yapısal ağaçtaki tek bir öğeyi (adım kartı ya da lane'li konteyner kutusu) render eder.
 * Konteyner lane'leri KENDİNE özyinelemeyle (`app-structured-item`) render edilir — böylece
 * `StructuredSequenceComponent` ile dairesel import kurulmaz.
 */
@Component({
  selector: 'app-structured-item',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './structured-item.component.html',
  styleUrls: ['./structured-item.component.scss'],
})
export class StructuredItemComponent {
  @Input({ required: true }) item!: StructuredItem;

  get container(): ContainerItem | null {
    return this.item.kind === 'container' ? this.item : null;
  }

  lanes(c: ContainerItem): LaneName[] {
    return lanesFor(c.type);
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
}
