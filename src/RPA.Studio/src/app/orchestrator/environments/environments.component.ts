import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrchestratorService } from '../orchestrator.service';
import { Environment } from '../orchestrator.models';

/**
 * Ortam yönetimi ekranı (WP-6.4, Spec Bölüm 5.5): Dev/Test/Prod ortamlarını listeler ve
 * yeni ortam oluşturur. /api/environments uçundan beslenir. Deployment governance
 * (publish → Test, approve → Prod) bu ortamları hedefler.
 */
@Component({
  selector: 'app-orchestrator-environments',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './environments.component.html',
})
export class EnvironmentsComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly environments = signal<Environment[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);

  newName = '';
  newDescription = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listEnvironments().subscribe({
      next: (e) => {
        this.environments.set(e);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Ortamlar yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  create(): void {
    const name = this.newName.trim();
    if (!name || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.service.createEnvironment(name, this.newDescription.trim() || undefined).subscribe({
      next: () => {
        this.newName = '';
        this.newDescription = '';
        this.saving.set(false);
        this.load();
      },
      error: () => {
        this.error.set('Ortam oluşturulamadı (ad benzersiz olmalı, yetki gerekir).');
        this.saving.set(false);
      },
    });
  }
}
