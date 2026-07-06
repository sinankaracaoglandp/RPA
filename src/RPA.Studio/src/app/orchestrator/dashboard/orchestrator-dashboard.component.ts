import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { OrchestratorService } from '../orchestrator.service';
import { DashboardSummary } from '../orchestrator.models';

/**
 * Orchestrator Dashboard (WP-6.1, Spec Bölüm 8.2): bugünkü işler, başarı oranı ve
 * durum kırılımı kartları. Read-side; /api/jobruns/dashboard uçundan beslenir.
 */
@Component({
  selector: 'app-orchestrator-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './orchestrator-dashboard.component.html',
  styleUrls: ['./orchestrator-dashboard.component.scss'],
})
export class OrchestratorDashboardComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly summary = signal<DashboardSummary | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getDashboard().subscribe({
      next: (s) => {
        this.summary.set(s);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Dashboard verileri yüklenemedi.');
        this.loading.set(false);
      },
    });
  }
}
