import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { OrchestratorService } from '../orchestrator.service';
import { QueueItem, QueueSummary } from '../orchestrator.models';

/**
 * Orchestrator Kuyruklar ekranı (WP-6.1, Spec Bölüm 8.2): kuyruk listesi + durum sayaçları;
 * bir kuyruk seçilince o kuyruğun kalemleri (opsiyonel durum filtresi) listelenir.
 */
@Component({
  selector: 'app-orchestrator-queues',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './queues.component.html',
})
export class QueuesComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly queues = signal<QueueSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly selectedQueueId = signal<string | null>(null);
  readonly items = signal<QueueItem[]>([]);
  readonly itemsLoading = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listQueues().subscribe({
      next: (q) => {
        this.queues.set(q);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Kuyruklar yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  selectQueue(id: string): void {
    this.selectedQueueId.set(id);
    this.itemsLoading.set(true);
    this.service.listQueueItems(id).subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.itemsLoading.set(false);
      },
      error: () => {
        this.items.set([]);
        this.itemsLoading.set(false);
      },
    });
  }
}
