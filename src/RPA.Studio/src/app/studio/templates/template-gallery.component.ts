import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '../../core/translate.pipe';
import { TemplateService } from '../../shared/services/template.service';
import { TemplateMetadata } from '../../shared/models/template.model';
import { WorkflowVersion } from '../../shared/models/workflow.model';
import { WorkflowDraftService } from '../../shared/services/workflow-draft.service';
import { TemplateCardComponent } from './template-card.component';
import { TemplateWizardComponent } from './template-wizard/template-wizard.component';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';

/** Sentinel category value meaning "no filter". */
const ALL_CATEGORIES = '__all__';

/**
 * Template Gallery — card grid of pre-built workflow templates.
 * Filter by category, search by name, click a card to open the creation
 * wizard (Faz 5, Task 5.5).
 */
@Component({
  selector: 'app-template-gallery',
  standalone: true,
  imports: [CommonModule, TranslatePipe, TemplateCardComponent, TemplateWizardComponent, BackHomeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './template-gallery.component.html',
  styleUrls: ['./template-gallery.component.scss'],
})
export class TemplateGalleryComponent {
  private readonly service = inject(TemplateService);
  private readonly draft = inject(WorkflowDraftService);
  private readonly router = inject(Router);

  readonly ALL_CATEGORIES = ALL_CATEGORIES;

  readonly templates = signal<TemplateMetadata[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly searchTerm = signal('');
  readonly selectedCategory = signal<string>(ALL_CATEGORIES);
  readonly selectedTemplate = signal<TemplateMetadata | null>(null);

  readonly categories = computed(() => {
    const set = new Set<string>();
    for (const t of this.templates()) {
      set.add(t.category);
    }
    return Array.from(set).sort();
  });

  readonly filteredTemplates = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const category = this.selectedCategory();
    return this.templates().filter((t) => {
      const matchesCategory = category === ALL_CATEGORIES || t.category === category;
      const matchesTerm = !term || t.name.toLowerCase().includes(term);
      return matchesCategory && matchesTerm;
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.service.getTemplates().subscribe({
      next: (templates) => {
        this.templates.set(templates ?? []);
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

  openWizard(template: TemplateMetadata): void {
    this.selectedTemplate.set(template);
  }

  closeWizard(): void {
    this.selectedTemplate.set(null);
  }

  onWorkflowCreated(workflow: WorkflowVersion): void {
    this.draft.setPending(workflow);
    this.closeWizard();
    void this.router.navigateByUrl('/designer');
  }

  trackByTemplateId(_index: number, template: TemplateMetadata): string {
    return template.id;
  }
}
