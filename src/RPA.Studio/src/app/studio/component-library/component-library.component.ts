import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { TranslatePipe } from '../../core/translate.pipe';
import { ComponentDetailService } from '../../shared/services/component-detail.service';
import { ComponentVersion } from '../../shared/models/component.model';

/**
 * Component library panel — displays published/draft components in a grid/list.
 * Filters by status, searches by name. Click to view details. Publish/Approve buttons.
 */
@Component({
  selector: 'app-component-library',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './component-library.component.html',
  styleUrls: ['./component-library.component.scss'],
})
export class ComponentLibraryComponent {
  private readonly service = inject(ComponentDetailService);

  readonly components = signal<ComponentVersion[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly searchTerm = signal('');
  readonly selectedFilter = signal<'All' | 'Draft' | 'Published'>('All');
  readonly selectedComponent = signal<ComponentVersion | null>(null);

  readonly filteredComponents = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const filter = this.selectedFilter();

    return this.components().filter((comp) => {
      const matchesFilter = filter === 'All' || comp.status === filter;
      const matchesTerm =
        !term ||
        comp.displayName.toLowerCase().includes(term) ||
        (comp.description?.toLowerCase().includes(term) ?? false);
      return matchesFilter && matchesTerm;
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.service.getComponents().subscribe({
      next: (comps) => {
        this.components.set(comps ?? []);
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

  selectFilter(filter: 'All' | 'Draft' | 'Published'): void {
    this.selectedFilter.set(filter);
  }

  selectComponent(comp: ComponentVersion): void {
    this.selectedComponent.set(comp);
  }

  closeDetails(): void {
    this.selectedComponent.set(null);
  }

  trackByComponentId(_index: number, comp: ComponentVersion): string {
    return comp.id;
  }
}
