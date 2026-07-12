import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrchestratorService } from '../orchestrator.service';
import { AlertRule } from '../orchestrator.models';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';

/**
 * Alarm Kuralları ekranı (WP-6.3, Spec Bölüm 8.2): kuralları listeler, aktif/pasif yapar ve
 * yeni kural oluşturur. Koşul bir metrik + eşik olarak seçilir. /api/alert-rules uçlarından beslenir.
 */
@Component({
  selector: 'app-alert-rules',
  standalone: true,
  imports: [CommonModule, FormsModule, BackHomeComponent],
  templateUrl: './alert-rules.component.html',
})
export class AlertRulesComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly rules = signal<AlertRule[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly metrics = ['SystemExceptionCount', 'BusinessExceptionCount', 'RobotOfflineCount', 'QueueSlaBreachCount'];
  readonly channels = ['email', 'teams'];

  form = { name: '', metric: 'SystemExceptionCount', threshold: 5, channel: 'email', recipients: '' };

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listAlertRules().subscribe({
      next: (r) => {
        this.rules.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Alarm kuralları yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  toggle(rule: AlertRule): void {
    this.service.setAlertRuleActive(rule.id, !rule.isActive).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Durum değiştirilemedi.'),
    });
  }

  create(): void {
    if (!this.form.name.trim()) {
      this.error.set('Kural adı zorunludur.');
      return;
    }
    const condition = JSON.stringify({ metric: this.form.metric, threshold: this.form.threshold });
    this.service
      .createAlertRule({
        name: this.form.name,
        condition,
        channel: this.form.channel,
        recipients: this.form.recipients,
        isActive: true,
      })
      .subscribe({
        next: () => {
          this.form = { name: '', metric: 'SystemExceptionCount', threshold: 5, channel: 'email', recipients: '' };
          this.load();
        },
        error: () => this.error.set('Kural oluşturulamadı.'),
      });
  }
}
