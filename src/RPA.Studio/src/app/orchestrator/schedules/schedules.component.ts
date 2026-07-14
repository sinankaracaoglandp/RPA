import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrchestratorService } from '../orchestrator.service';
import { CreateTriggerRequest, Robot, TriggerDefinition } from '../orchestrator.models';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';

/**
 * Orchestrator Zamanlamalar ekranı: job (Trigger) tanımlarını listeler, yeni job oluşturur,
 * aktif/pasif değiştirir, manuel çalıştırır. Hangi ajanın koşacağı TargetRobotTags ile burada belirlenir.
 */
@Component({
  selector: 'app-orchestrator-schedules',
  standalone: true,
  imports: [CommonModule, FormsModule, BackHomeComponent],
  templateUrl: './schedules.component.html',
})
export class SchedulesComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly triggers = signal<TriggerDefinition[]>([]);
  readonly robots = signal<Robot[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showForm = signal(false);

  readonly triggerTypes = ['Cron', 'ApiWebhook', 'Manual'];

  form: CreateTriggerRequest = this.emptyForm();

  ngOnInit(): void {
    this.load();
    this.service.listRobots().subscribe({ next: (r) => this.robots.set(r) });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listTriggers().subscribe({
      next: (t) => {
        this.triggers.set(t);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Zamanlamalar yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  toggleForm(): void {
    this.showForm.update((v) => !v);
    if (this.showForm()) this.form = this.emptyForm();
  }

  save(): void {
    this.service.createTrigger(this.form).subscribe({
      next: () => {
        this.showForm.set(false);
        this.load();
      },
      error: () => this.error.set('Job oluşturulamadı.'),
    });
  }

  setActive(t: TriggerDefinition, isActive: boolean): void {
    this.service.updateTrigger(t.id, { isActive }).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Durum güncellenemedi.'),
    });
  }

  runNow(t: TriggerDefinition): void {
    this.service.fireTrigger(t.id).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Çalıştırma başarısız.'),
    });
  }

  private emptyForm(): CreateTriggerRequest {
    return {
      projectId: '',
      workflowVersionId: '',
      type: 'Manual',
      environmentId: '',
      isActive: true,
      targetRobotTags: '',
      priority: 0,
    };
  }
}
