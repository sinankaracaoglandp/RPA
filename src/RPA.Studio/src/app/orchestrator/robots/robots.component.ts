import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { OrchestratorService } from '../orchestrator.service';
import { Robot } from '../orchestrator.models';

/**
 * Orchestrator Robotlar ekranı (WP-6.1, Spec Bölüm 8.2): kayıtlı robotlar + sağlık/mod.
 * /api/robots uçundan beslenir.
 */
@Component({
  selector: 'app-orchestrator-robots',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './robots.component.html',
})
export class RobotsComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly robots = signal<Robot[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listRobots().subscribe({
      next: (r) => {
        this.robots.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Robotlar yüklenemedi.');
        this.loading.set(false);
      },
    });
  }
}
