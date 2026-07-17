import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { BackHomeComponent } from '../../shared/back-home/back-home.component';
import { TranslatePipe } from '../../core/translate.pipe';
import { LicenseStatus } from '../licensing/license.models';
import { LicenseService, apiErrorMessage } from '../licensing/license.service';
import { ActivationCodeResponse, AgentDto, RotateCredentialResponse, consumesSeat } from './agent-license.models';
import { AgentLicenseService } from './agent-license.service';

/**
 * Orchestrator aktive agent yonetimi ekrani (Offline Agent Licensing — Task 8).
 * Agent durumlarini rozetlerle listeler, bekleyen agent olusturur, tek kullanimlik
 * aktivasyon kodu uretir, devre disi birakma/aktivasyon geri alma islemlerini onaylatir.
 *
 * Guvenlik/kontrat kurallari:
 * - Aktivasyon kodu YALNIZCA bir kez gosterilir; state sadece bellekte (signal) tutulur ve
 *   kapatmada/gezinmede (ngOnDestroy) temizlenir. local/sessionStorage'a asla yazilmaz.
 * - Koltuk kullanimi ISTEMCIDE HESAPLANMAZ; her mutasyondan sonra WebAPI'den yeniden okunur
 *   (`GET /api/license/status`) — WebAPI tek yetkili kaynaktir.
 */
@Component({
  selector: 'app-orchestrator-agents',
  standalone: true,
  imports: [CommonModule, FormsModule, BackHomeComponent, TranslatePipe],
  templateUrl: './agent-license-page.component.html',
})
export class AgentLicensePageComponent implements OnInit, OnDestroy {
  private readonly agentsApi = inject(AgentLicenseService);
  private readonly licenseApi = inject(LicenseService);

  readonly agents = signal<AgentDto[]>([]);
  readonly status = signal<LicenseStatus | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly newName = signal('');

  /** Tek seferlik aktivasyon kodu — yalnizca bellekte, kapatinca kaybolur. */
  readonly activationCode = signal<ActivationCodeResponse | null>(null);

  /** Tek seferlik yeni credential (rotasyon) — yalnizca bellekte, kapatinca kaybolur. */
  readonly rotatedCredential = signal<RotateCredentialResponse | null>(null);

  readonly consumesSeat = consumesSeat;

  ngOnInit(): void {
    this.refresh();
  }

  /** Gezinmede tek-gosterim sir state'lerini bellekte birakma. */
  ngOnDestroy(): void {
    this.activationCode.set(null);
    this.rotatedCredential.set(null);
  }

  /** Agent listesi + YETKILI koltuk durumu; her mutasyondan sonra da cagrilir. */
  refresh(): void {
    this.loading.set(true);
    this.error.set(null);

    this.agentsApi.list().subscribe({
      next: (list) => {
        this.agents.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, 'Agentlar yuklenemedi.'));
        this.loading.set(false);
      },
    });

    this.licenseApi.getStatus().subscribe({
      next: (s) => this.status.set(s),
      error: () => this.status.set(null),
    });
  }

  create(): void {
    const name = this.newName().trim();
    if (!name) return;

    this.busy.set(true);
    this.agentsApi.create(name).subscribe({
      next: () => {
        this.newName.set('');
        this.busy.set(false);
        this.refresh();
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, 'Agent olusturulamadi.'));
        this.busy.set(false);
      },
    });
  }

  showActivationCode(agent: AgentDto): void {
    this.busy.set(true);
    this.agentsApi.createActivationCode(agent.id).subscribe({
      next: (response) => {
        this.activationCode.set(response);
        this.busy.set(false);
        this.refresh();
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, 'Aktivasyon kodu uretilemedi.'));
        this.busy.set(false);
      },
    });
  }

  closeActivationCode(): void {
    this.activationCode.set(null);
  }

  /**
   * Credential degistir — ESKI CREDENTIAL DERHAL GECERSIZLESIR; kullanicidan onay alinir.
   * Yeni credential yalnizca bir kez, bellekte gosterilir (depolamaya yazilmaz).
   */
  rotateCredential(agent: AgentDto): void {
    if (!window.confirm(
      `"${agent.name}" icin yeni credential uretilecek. Mevcut credential DERHAL gecersiz olacak ` +
      `ve agent yeni credential ile yeniden yapilandirilana kadar baglanamayacak. Onayliyor musunuz?`,
    )) {
      return;
    }

    this.busy.set(true);
    this.agentsApi.rotateCredential(agent.id).subscribe({
      next: (response) => {
        this.rotatedCredential.set(response);
        this.busy.set(false);
        this.refresh();
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, 'Agent credential degistirilemedi.'));
        this.busy.set(false);
      },
    });
  }

  closeRotatedCredential(): void {
    this.rotatedCredential.set(null);
  }

  /** Devre disi birak — koltuk KORUNUR; kullanicidan onay alinir. */
  disable(agent: AgentDto): void {
    if (!window.confirm(`"${agent.name}" devre disi birakilacak. Lisans koltugu korunur. Onayliyor musunuz?`)) {
      return;
    }
    this.mutate(this.agentsApi.disable(agent.id), 'Agent devre disi birakilamadi.');
  }

  /** Aktivasyonu geri al — koltuk SERBEST kalir; kullanicidan onay alinir. */
  deactivate(agent: AgentDto): void {
    if (!window.confirm(`"${agent.name}" aktivasyonu geri alinacak. Lisans koltugu serbest birakilir. Onayliyor musunuz?`)) {
      return;
    }
    this.mutate(this.agentsApi.deactivate(agent.id), 'Agent aktivasyonu geri alinamadi.');
  }

  private mutate(call: import('rxjs').Observable<void>, fallback: string): void {
    this.busy.set(true);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.refresh();
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err, fallback));
        this.busy.set(false);
      },
    });
  }
}
