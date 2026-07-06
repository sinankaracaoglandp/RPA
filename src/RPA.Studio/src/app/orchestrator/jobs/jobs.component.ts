import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrchestratorService } from '../orchestrator.service';
import { JobRun } from '../orchestrator.models';

/**
 * Orchestrator İşler ekranı (WP-6.1, Spec Bölüm 8.2): JobRun listesi + durum filtresi.
 * /api/jobruns uçundan sayfalı beslenir.
 */
@Component({
  selector: 'app-orchestrator-jobs',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './jobs.component.html',
})
export class JobsComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly jobs = signal<JobRun[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  statusFilter = '';

  readonly statuses = ['', 'Running', 'Successful', 'Failed', 'BusinessException', 'Abandoned'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listJobs({ status: this.statusFilter || undefined, take: 50 }).subscribe({
      next: (r) => {
        this.jobs.set(r.items);
        this.total.set(r.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('İşler yüklenemedi.');
        this.loading.set(false);
      },
    });
  }
}
