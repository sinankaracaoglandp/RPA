import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';
import { CredentialReference } from '../orchestrator.models';
import { OrchestratorService } from '../orchestrator.service';

@Component({
  selector: 'app-orchestrator-credentials',
  standalone: true,
  imports: [CommonModule, FormsModule, BackHomeComponent],
  templateUrl: './credentials.component.html',
})
export class CredentialsComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly credentials = signal<CredentialReference[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly deletingKey = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly success = signal<string | null>(null);

  key = '';
  type = 'SAP';
  environment = 'DEV';
  description = '';
  secret = '';

  readonly credentialTypes = ['SAP', 'Web', 'API', 'Email', 'TOTP'];
  readonly environments = ['DEV', 'TEST', 'PROD'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listCredentials().subscribe({
      next: (credentials) => {
        this.credentials.set(credentials);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Credential referanslari yuklenemedi.');
        this.loading.set(false);
      },
    });
  }

  store(): void {
    const key = this.key.trim();
    const secret = this.secret;
    if (!key || !secret || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.success.set(null);
    this.service.storeCredential({
      key,
      secret,
      type: this.type,
      environment: this.environment,
      description: this.description.trim() || undefined,
    }).subscribe({
      next: () => {
        this.secret = '';
        this.key = '';
        this.description = '';
        this.saving.set(false);
        this.success.set('Credential Vault icine kaydedildi. Secret degeri ekranda tutulmadi.');
        this.load();
      },
      error: () => {
        this.error.set('Credential kaydedilemedi. Vault ayarlarini ve yetkiyi kontrol edin.');
        this.saving.set(false);
      },
    });
  }

  deleteCredential(key: string): void {
    if (!key || this.deletingKey()) {
      return;
    }

    this.deletingKey.set(key);
    this.error.set(null);
    this.success.set(null);
    this.service.deleteCredential(key).subscribe({
      next: () => {
        this.deletingKey.set(null);
        this.success.set('Credential referansi silindi.');
        this.load();
      },
      error: () => {
        this.deletingKey.set(null);
        this.error.set('Credential silinemedi.');
      },
    });
  }
}
