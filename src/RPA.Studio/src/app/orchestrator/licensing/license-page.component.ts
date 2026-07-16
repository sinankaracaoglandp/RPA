import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';

import { BackHomeComponent } from '../../shared/back-home/back-home.component';
import { TranslatePipe } from '../../core/translate.pipe';
import { LicenseStatus, SignedLicenseDocument } from './license.models';
import { LicenseService, apiErrorMessage } from './license.service';

/**
 * Orchestrator lisans yonetimi ekrani (Offline Agent Licensing — Task 7).
 * Lisans kimligi/surumu/gecerliligi/ozellikleri + koltuk kullanimi gosterir,
 * kurulum talebini indirir ve imzali .lic belgesini iceri alir.
 * Guvenlik: lisans/gizli veriler hicbir zaman local/sessionStorage'a yazilmaz.
 */
@Component({
  selector: 'app-orchestrator-licensing',
  standalone: true,
  imports: [CommonModule, BackHomeComponent, TranslatePipe],
  templateUrl: './license-page.component.html',
  styleUrl: './license-page.component.css',
})
export class LicensePageComponent implements OnInit {
  private readonly service = inject(LicenseService);

  readonly status = signal<LicenseStatus | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly importError = signal<string | null>(null);
  readonly importSuccess = signal(false);
  readonly busy = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getStatus().subscribe({
      next: (s) => {
        this.status.set(s);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, 'Lisans durumu yuklenemedi.'));
        this.loading.set(false);
      },
    });
  }

  /** Kurulum talebini JSON olarak indirir; object URL indirme sonrasi serbest birakilir. */
  downloadInstallationRequest(): void {
    this.busy.set(true);
    this.service.getInstallationRequest().subscribe({
      next: (request) => {
        const blob = new Blob([JSON.stringify(request, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        try {
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = `installation-request-${request.installationId}.json`;
          anchor.click();
        } finally {
          URL.revokeObjectURL(url);
        }
        this.busy.set(false);
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, 'Kurulum talebi indirilemedi.'));
        this.busy.set(false);
      },
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      void this.importFile(file);
    }
  }

  /** Secilen .lic dosyasini ayristirir ve API'ye gonderir. Dosya icerigi bellekte kalir. */
  async importFile(file: File): Promise<void> {
    this.importError.set(null);
    this.importSuccess.set(false);

    let document: SignedLicenseDocument;
    try {
      document = JSON.parse(await file.text()) as SignedLicenseDocument;
    } catch {
      this.importError.set('Lisans dosyasi okunamadi: gecerli bir JSON degil.');
      return;
    }

    this.busy.set(true);
    this.service.import(document).subscribe({
      next: (status) => {
        this.status.set(status);
        this.importSuccess.set(true);
        this.busy.set(false);
        this.load();
      },
      error: (err) => {
        this.importError.set(apiErrorMessage(err, 'Lisans iceri alinamadi.'));
        this.busy.set(false);
      },
    });
  }
}
