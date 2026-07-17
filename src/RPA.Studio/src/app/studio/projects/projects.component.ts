import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '../../core/translate.pipe';
import {
  ProjectService,
  ProjectSummary,
  WorkflowSummary,
} from '../../shared/services/project.service';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';

/** Projelerim: proje kartları → workflow listesi → designer'a aç (Paket B). */
@Component({
  selector: 'app-projects',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, BackHomeComponent],
  templateUrl: './projects.component.html',
  styleUrls: ['./projects.component.scss'],
})
export class ProjectsComponent implements OnInit {
  private readonly projectService = inject(ProjectService);
  private readonly router = inject(Router);

  readonly projects = signal<ProjectSummary[]>([]);
  readonly workflows = signal<WorkflowSummary[]>([]);
  readonly selectedProjectId = signal<string | null>(null);
  readonly newProjectName = signal('');
  readonly newWorkflowName = signal('');
  readonly error = signal<string | null>(null);

  get selectedProject(): ProjectSummary | undefined {
    return this.projects().find((project) => project.id === this.selectedProjectId());
  }

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.projectService.getProjects().subscribe({
      next: (list) => this.projects.set(list),
      error: () => this.error.set('projects.loadError'),
    });
  }

  createProject(): void {
    const name = this.newProjectName().trim();
    if (!name) {
      return;
    }
    this.projectService.createProject(name).subscribe({
      next: () => {
        this.newProjectName.set('');
        this.refresh();
      },
      error: () => this.error.set('projects.createError'),
    });
  }

  openProject(projectId: string): void {
    this.selectedProjectId.set(projectId);
    this.projectService.getWorkflows(projectId).subscribe({
      next: (list) => this.workflows.set(list),
      error: () => this.error.set('projects.loadError'),
    });
  }

  createWorkflow(): void {
    const projectId = this.selectedProjectId();
    const name = this.newWorkflowName().trim();
    if (!projectId || !name) {
      return;
    }
    this.projectService.createWorkflow(projectId, name).subscribe({
      next: (wf) => {
        this.newWorkflowName.set('');
        this.openWorkflow(wf.id);
      },
      error: () => this.error.set('projects.createError'),
    });
  }

  openWorkflow(workflowId: string): void {
    // Projeyi query param olarak taşı — designer, e-fatura profil seçici gibi
    // proje-kapsamlı alanları elle GUID girmeden otomatik doldurabilsin.
    const projectId = this.selectedProjectId();
    void this.router.navigate(['/designer', workflowId], projectId ? { queryParams: { projectId } } : {});
  }

  openEInvoiceProfiles(): void {
    const projectId = this.selectedProjectId();
    if (projectId) {
      void this.router.navigate(['/projects', projectId, 'einvoice-profiles']);
    }
  }
}
