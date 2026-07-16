import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  EInvoiceProfile,
  EInvoiceProfileVersion,
} from '../../../shared/models/einvoice-profile.model';
import { EInvoiceProfileService } from '../../../shared/services/einvoice-profile.service';
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

  readonly projectId = signal('');
  readonly profiles = signal<EInvoiceProfile[]>([]);
  readonly versions = signal<EInvoiceProfileVersion[]>([]);
  readonly selectedProfileId = signal<string | null>(null);
  readonly draftJson = signal('');
  readonly newName = signal('');
  readonly newDescription = signal('');
  readonly error = signal<string | null>(null);

  get selectedProfile(): EInvoiceProfile | undefined {
    return this.profiles().find((profile) => profile.id === this.selectedProfileId());
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      this.projectId.set(params.get('projectId') ?? '');
      this.refresh();
    });
  }

  refresh(): void {
    const projectId = this.projectId();
    if (!projectId) {
      return;
    }
    this.profilesApi.list(projectId).subscribe({
      next: (profiles) => this.profiles.set(profiles),
      error: () => this.error.set('E-fatura profilleri yüklenemedi.'),
    });
  }

  createProfile(): void {
    const name = this.newName().trim();
    if (!name) {
      return;
    }
    this.profilesApi.create(this.projectId(), { name, description: this.newDescription().trim() || null }).subscribe({
      next: (profile) => {
        this.newName.set('');
        this.newDescription.set('');
        this.profiles.update((profiles) => [...profiles, profile]);
        this.openDraft(profile.id);
      },
      error: () => this.error.set('Profil oluşturulamadı.'),
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
    this.profilesApi.saveDraft(this.projectId(), profileId, this.draftJson()).subscribe({
      next: (profile) => this.profiles.update((profiles) => profiles.map((item) => item.id === profile.id ? profile : item)),
      error: () => this.error.set('Taslak kaydedilemedi.'),
    });
  }

  publish(profileId: string): void {
    this.profilesApi.publish(this.projectId(), profileId).subscribe({
      next: () => this.loadVersions(profileId),
      error: () => this.error.set('Profil yayınlanamadı.'),
    });
  }

  latestVersion(profileId: string): number | null {
    const version = this.versions().find((item) => item.profileId === profileId);
    return version?.version ?? null;
  }

  private loadVersions(profileId: string): void {
    this.profilesApi.versions(this.projectId(), profileId).subscribe({
      next: (versions) => this.versions.set(versions),
      error: () => this.versions.set([]),
    });
  }
}
