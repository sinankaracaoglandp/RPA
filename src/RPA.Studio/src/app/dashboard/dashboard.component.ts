import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '../core/translate.pipe';
import { SupportedLang, TranslationService } from '../core/translation.service';
import { AuthService } from '../auth/auth.service';

interface NavCard {
  route: string;
  titleKey: string;
  descKey: string;
  icon: string;
  accent: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslatePipe],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  readonly translationService = inject(TranslationService);

  readonly username = this.authService.getUsername();
  readonly roles = this.authService.getRoles();
  readonly rolesCount = computed(() => this.roles.length);

  readonly studioCards: NavCard[] = [
    {
      route: '/projects',
      titleKey: 'projects.title',
      descKey: 'dashboard.sectionStudioDesc',
      accent: 'bg-fuchsia-50 text-fuchsia-600',
      icon: 'M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-16.5 0v6a2.25 2.25 0 0 0 2.25 2.25h13.5A2.25 2.25 0 0 0 21.75 18.75v-6m-16.5 0h16.5M6.75 9.75V6a2.25 2.25 0 0 1 2.25-2.25h6a2.25 2.25 0 0 1 2.25 2.25v3.75',
    },
    {
      route: '/designer',
      titleKey: 'dashboard.designerTitle',
      descKey: 'dashboard.designerDesc',
      accent: 'bg-blue-50 text-blue-600',
      icon: 'M9.75 3.104v5.714a2.25 2.25 0 0 1-.659 1.591L5 14.5M9.75 3.104c-.251.023-.501.05-.75.082m.75-.082a24.301 24.301 0 0 1 4.5 0m0 0v5.714c0 .597.237 1.17.659 1.591L19.8 15.3M14.25 3.104c.251.023.501.05.75.082M19.8 15.3l-1.57.393A9.065 9.065 0 0 1 12 15a9.065 9.065 0 0 0-6.23-.693L5 14.5m14.8.8 1.402 1.402c1.232 1.232.65 3.318-1.067 3.611A48.309 48.309 0 0 1 12 21c-2.773 0-5.491-.235-8.135-.687-1.718-.293-2.3-2.379-1.067-3.61L5 14.5',
    },
    {
      route: '/einvoice-addressing',
      titleKey: 'dashboard.einvoiceAddressingTitle',
      descKey: 'dashboard.einvoiceAddressingDesc',
      accent: 'bg-amber-50 text-amber-700',
      icon: 'M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5A3.375 3.375 0 0 0 10.125 2.25H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Zm-1.5 9h6m-6 3h6m-6 3h3',
    },
    {
      route: '/component-library',
      titleKey: 'dashboard.componentLibraryTitle',
      descKey: 'dashboard.componentLibraryDesc',
      accent: 'bg-indigo-50 text-indigo-600',
      icon: 'M20.25 6.375c0 2.278-3.694 4.125-8.25 4.125S3.75 8.653 3.75 6.375m16.5 0c0-2.278-3.694-4.125-8.25-4.125S3.75 4.097 3.75 6.375m16.5 0v11.25c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125V6.375m16.5 0v3.75m-16.5-3.75v3.75m16.5 0v3.75C20.25 16.153 16.556 18 12 18s-8.25-1.847-8.25-4.125v-3.75',
    },
    {
      route: '/templates',
      titleKey: 'dashboard.templatesTitle',
      descKey: 'dashboard.templatesDesc',
      accent: 'bg-violet-50 text-violet-600',
      icon: 'M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-8.69-6.44-2.12-2.12a1.5 1.5 0 0 0-1.061-.44H4.5A2.25 2.25 0 0 0 2.25 6v12a2.25 2.25 0 0 0 2.25 2.25h15A2.25 2.25 0 0 0 21.75 18V9a2.25 2.25 0 0 0-2.25-2.25h-5.379a1.5 1.5 0 0 1-1.06-.44Z',
    },
  ];

  readonly orchestratorCards: NavCard[] = [
    {
      route: '/orchestrator',
      titleKey: 'dashboard.orchestratorTitle',
      descKey: 'dashboard.orchestratorDesc',
      accent: 'bg-sky-50 text-sky-600',
      icon: 'M3.75 3v11.25A2.25 2.25 0 0 0 6 16.5h2.25M3.75 3h-1.5m1.5 0h16.5m0 0h1.5m-1.5 0v11.25A2.25 2.25 0 0 1 18 16.5h-2.25m-7.5 0h7.5m-7.5 0-1 3m8.5-3 1 3m0 0 .5 1.5m-.5-1.5h-9.5m0 0-.5 1.5M9 11.25v1.5M12 9v3.75m3-6v6',
    },
    {
      route: '/orchestrator/jobs',
      titleKey: 'dashboard.jobsTitle',
      descKey: 'dashboard.jobsDesc',
      accent: 'bg-emerald-50 text-emerald-600',
      icon: 'M6 6.878V6a2.25 2.25 0 0 1 2.25-2.25h7.5A2.25 2.25 0 0 1 18 6v.878m-12 0c.235-.083.487-.128.75-.128h10.5c.263 0 .515.045.75.128m-12 0A2.25 2.25 0 0 0 4.5 9v.878m13.5-3A2.25 2.25 0 0 1 19.5 9v.878m0 0a2.246 2.246 0 0 0-.75-.128H5.25c-.263 0-.515.045-.75.128m15 0A2.25 2.25 0 0 1 21 12v6a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 18v-6c0-.98.626-1.813 1.5-2.122',
    },
    {
      route: '/orchestrator/robots',
      titleKey: 'dashboard.robotsTitle',
      descKey: 'dashboard.robotsDesc',
      accent: 'bg-teal-50 text-teal-600',
      icon: 'M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 0 0-3.09 3.09ZM18.259 8.715 18 9.75l-.259-1.035a3.375 3.375 0 0 0-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 0 0 2.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 0 0 2.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 0 0-2.456 2.456Z',
    },
    {
      route: '/orchestrator/queues',
      titleKey: 'dashboard.queuesTitle',
      descKey: 'dashboard.queuesDesc',
      accent: 'bg-amber-50 text-amber-600',
      icon: 'M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M5.625 4.5h12.75a1.875 1.875 0 0 1 0 3.75H5.625a1.875 1.875 0 0 1 0-3.75Z',
    },
    {
      route: '/orchestrator/action-center',
      titleKey: 'dashboard.actionCenterTitle',
      descKey: 'dashboard.actionCenterDesc',
      accent: 'bg-rose-50 text-rose-600',
      icon: 'M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z',
    },
    {
      route: '/orchestrator/alert-rules',
      titleKey: 'dashboard.alertRulesTitle',
      descKey: 'dashboard.alertRulesDesc',
      accent: 'bg-orange-50 text-orange-600',
      icon: 'M14.857 17.082a23.848 23.848 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 0a3 3 0 1 1-5.714 0',
    },
    {
      route: '/orchestrator/environments',
      titleKey: 'dashboard.environmentsTitle',
      descKey: 'dashboard.environmentsDesc',
      accent: 'bg-cyan-50 text-cyan-600',
      icon: 'M12 21a9.004 9.004 0 0 0 8.716-6.747M12 21a9.004 9.004 0 0 1-8.716-6.747M12 21c2.485 0 4.5-4.03 4.5-9S14.485 3 12 3m0 18c-2.485 0-4.5-4.03-4.5-9S9.515 3 12 3m0 0a8.997 8.997 0 0 1 7.843 4.582M12 3a8.997 8.997 0 0 0-7.843 4.582m15.686 0A11.953 11.953 0 0 1 12 10.5c-2.998 0-5.74-1.1-7.843-2.918m15.686 0A8.959 8.959 0 0 1 21 12c0 .778-.099 1.533-.284 2.253m0 0A17.919 17.919 0 0 1 12 16.5c-3.162 0-6.133-.815-8.716-2.247m0 0A9.015 9.015 0 0 1 3 12c0-1.605.42-3.113 1.157-4.418',
    },
    {
      route: '/orchestrator/credentials',
      titleKey: 'dashboard.credentialsTitle',
      descKey: 'dashboard.credentialsDesc',
      accent: 'bg-lime-50 text-lime-700',
      icon: 'M15.75 5.25a3 3 0 1 1-4.243 4.243L6.75 14.25H4.5v2.25H2.25v2.25H0v2.25h3.75l9.507-9.507A3 3 0 0 1 15.75 5.25Z',
    },
    {
      route: '/orchestrator/licensing',
      titleKey: 'dashboard.licensingTitle',
      descKey: 'dashboard.licensingDesc',
      accent: 'bg-slate-100 text-slate-700',
      icon: 'M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285Z',
    },
    {
      route: '/orchestrator/agents',
      titleKey: 'dashboard.agentsTitle',
      descKey: 'dashboard.agentsDesc',
      accent: 'bg-teal-50 text-teal-700',
      icon: 'M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z',
    },
  ];

  changeLanguage(lang: SupportedLang): void {
    void this.translationService.use(lang);
  }

  logout(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/login');
  }
}
