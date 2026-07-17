import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  EInvoiceProfile,
  EInvoiceProfileVersion,
} from '../../../shared/models/einvoice-profile.model';
import { EInvoiceProfileService } from '../../../shared/services/einvoice-profile.service';
import { ProjectService, ProjectSummary } from '../../../shared/services/project.service';
import { EInvoiceMappingEditorComponent } from '../../designer/properties/einvoice-mapping-editor.component';

@Component({
  selector: 'app-einvoice-profiles',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, EInvoiceMappingEditorComponent],
  templateUrl: './einvoice-profiles.component.html',
  styleUrls: ['./einvoice-profiles.component.scss'],
})
export class EInvoiceProfilesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly profilesApi = inject(EInvoiceProfileService);
  private readonly projectApi = inject(ProjectService);

  readonly projectId = signal('');
  readonly projects = signal<ProjectSummary[]>([]);
  readonly profiles = signal<EInvoiceProfile[]>([]);
  readonly versions = signal<EInvoiceProfileVersion[]>([]);
  readonly selectedProfileId = signal<string | null>(null);
  readonly draftJson = signal('');
  readonly newName = signal('');
  readonly newDescription = signal('');
  readonly error = signal<string | null>(null);
  readonly savedNotice = signal<string | null>(null);
  private savedTimer: ReturnType<typeof setTimeout> | null = null;

  get selectedProfile(): EInvoiceProfile | undefined {
    return this.profiles().find((profile) => profile.id === this.selectedProfileId());
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const projectId = params.get('projectId') ?? '';
      this.projectId.set(projectId);
      if (projectId) {
        this.refresh();
      } else {
        this.loadProjects();
      }
    });
  }

  selectProject(projectId: string): void {
    this.projectId.set(projectId);
    this.selectedProfileId.set(null);
    this.draftJson.set('');
    this.versions.set([]);
    this.profiles.set([]);
    this.error.set(null);
    this.refresh();
  }

  refresh(): void {
    const projectId = this.projectId();
    if (!projectId) {
      return;
    }
    this.profilesApi.list(projectId).subscribe({
      next: (profiles) => this.profiles.set(profiles),
      error: (error) => this.error.set(this.toUserError(error, 'E-fatura profilleri yüklenemedi.')),
    });
  }

  createProfile(): void {
    const name = this.newName().trim();
    if (!name) {
      return;
    }
    const projectId = this.projectId();
    if (!projectId) {
      this.error.set('Profil oluşturmadan önce adresleme yapılacak projeyi seç.');
      return;
    }
    this.profilesApi.create(projectId, { name, description: this.newDescription().trim() || null }).subscribe({
      next: (profile) => {
        this.newName.set('');
        this.newDescription.set('');
        this.profiles.update((profiles) => [...profiles, profile]);
        this.openDraft(profile.id);
      },
      error: (error) => this.error.set(this.toUserError(error, 'Profil oluşturulamadı.')),
    });
  }

  openDraft(profileId: string): void {
    const profile = this.profiles().find((item) => item.id === profileId);
    if (!profile) {
      return;
    }
    this.selectedProfileId.set(profile.id);
    this.draftJson.set(profile.draftDefinitionJson || '{"fields":[],"collections":[]}');
    this.loadVersions(profile.id);
  }

  saveDraft(): void {
    const profileId = this.selectedProfileId();
    if (!profileId) {
      return;
    }
    const projectId = this.projectId();
    if (!projectId) {
      this.error.set('Taslak kaydetmeden önce adresleme yapılacak projeyi seç.');
      return;
    }
    this.profilesApi.saveDraft(projectId, profileId, this.draftJson()).subscribe({
      next: (profile) => {
        this.profiles.update((profiles) => profiles.map((item) => item.id === profile.id ? profile : item));
        this.flashSaved('Taslak kaydedildi.');
      },
      error: (error) => this.error.set(this.toUserError(error, 'Taslak kaydedilemedi.')),
    });
  }

  publish(profileId: string): void {
    const projectId = this.projectId();
    if (!projectId) {
      this.error.set('Profil yayınlamadan önce adresleme yapılacak projeyi seç.');
      return;
    }
    this.profilesApi.publish(projectId, profileId).subscribe({
      next: () => {
        this.loadVersions(profileId);
        this.flashSaved('Profil yayınlandı.');
      },
      error: (error) => this.error.set(this.toUserError(error, 'Profil yayınlanamadı.')),
    });
  }

  latestVersion(profileId: string): number | null {
    const version = this.versions().find((item) => item.profileId === profileId);
    return version?.version ?? null;
  }

  private loadVersions(profileId: string): void {
    const projectId = this.projectId();
    if (!projectId) {
      this.versions.set([]);
      return;
    }
    this.profilesApi.versions(projectId, profileId).subscribe({
      next: (versions) => this.versions.set(versions),
      error: () => this.versions.set([]),
    });
  }

  private loadProjects(): void {
    this.projectApi.getProjects().subscribe({
      next: (projects) => this.projects.set(projects),
      error: (error) => this.error.set(this.toUserError(error, 'Projeler yüklenemedi.')),
    });
  }

  private flashSaved(message: string): void {
    this.error.set(null);
    this.savedNotice.set(message);
    if (this.savedTimer) {
      clearTimeout(this.savedTimer);
    }
    this.savedTimer = setTimeout(() => this.savedNotice.set(null), 3000);
  }

  private toUserError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      return 'Oturum süresi doldu. Lütfen tekrar giriş yap.';
    }
    return fallback;
  }
}
