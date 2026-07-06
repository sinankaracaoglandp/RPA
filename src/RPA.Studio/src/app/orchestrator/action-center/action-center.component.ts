import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrchestratorService } from '../orchestrator.service';
import { ActionItem } from '../orchestrator.models';

/**
 * Action Center ekranı (WP-6.2, Spec Bölüm 8.2): bekleyen BusinessException / OTP / Onay
 * kayıtları; type filtresi + çözümleme (not ile). /api/action-center uçlarından beslenir.
 */
@Component({
  selector: 'app-action-center',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './action-center.component.html',
})
export class ActionCenterComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly items = signal<ActionItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  typeFilter = '';
  readonly resolvingId = signal<string | null>(null);
  resolveNote = '';

  readonly types = ['', 'BusinessException', 'OtpRequest', 'Approval'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listActionItems(this.typeFilter || undefined).subscribe({
      next: (i) => {
        this.items.set(i);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Action Center kayıtları yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  startResolve(id: string): void {
    this.resolvingId.set(id);
    this.resolveNote = '';
  }

  cancelResolve(): void {
    this.resolvingId.set(null);
  }

  confirmResolve(id: string): void {
    this.service.resolveActionItem(id, this.resolveNote).subscribe({
      next: () => {
        this.resolvingId.set(null);
        this.load();
      },
      error: () => this.error.set('Kayıt çözümlenemedi.'),
    });
  }
}
