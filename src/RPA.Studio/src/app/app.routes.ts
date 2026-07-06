import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { LoginComponent } from './auth/login.component';
import { DashboardComponent } from './dashboard/dashboard.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { path: '', component: DashboardComponent, canActivate: [authGuard] },
  {
    path: 'designer',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./studio/designer/designer.component').then((m) => m.DesignerComponent),
  },
  {
    path: 'component-library',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./studio/component-library/component-library.component').then((m) => m.ComponentLibraryComponent),
  },
  {
    path: 'templates',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./studio/templates/template-gallery.component').then((m) => m.TemplateGalleryComponent),
  },
  {
    path: 'orchestrator',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./orchestrator/dashboard/orchestrator-dashboard.component').then((m) => m.OrchestratorDashboardComponent),
  },
  {
    path: 'orchestrator/jobs',
    canActivate: [authGuard],
    loadComponent: () => import('./orchestrator/jobs/jobs.component').then((m) => m.JobsComponent),
  },
  {
    path: 'orchestrator/robots',
    canActivate: [authGuard],
    loadComponent: () => import('./orchestrator/robots/robots.component').then((m) => m.RobotsComponent),
  },
  { path: '**', redirectTo: '' },
];
