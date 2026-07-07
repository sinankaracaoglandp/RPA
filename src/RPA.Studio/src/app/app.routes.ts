import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { LoginComponent } from './auth/login.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { dirtyGuard } from './studio/designer/dirty-guard';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { path: '', component: DashboardComponent, canActivate: [authGuard] },
  {
    path: 'designer',
    canActivate: [authGuard],
    canDeactivate: [dirtyGuard],
    loadComponent: () =>
      import('./studio/designer/designer.component').then((m) => m.DesignerComponent),
  },
  {
    path: 'designer/:workflowId',
    canActivate: [authGuard],
    canDeactivate: [dirtyGuard],
    loadComponent: () =>
      import('./studio/designer/designer.component').then((m) => m.DesignerComponent),
  },
  {
    path: 'projects',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./studio/projects/projects.component').then((m) => m.ProjectsComponent),
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
  {
    path: 'orchestrator/queues',
    canActivate: [authGuard],
    loadComponent: () => import('./orchestrator/queues/queues.component').then((m) => m.QueuesComponent),
  },
  {
    path: 'orchestrator/action-center',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./orchestrator/action-center/action-center.component').then((m) => m.ActionCenterComponent),
  },
  {
    path: 'orchestrator/alert-rules',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./orchestrator/alert-rules/alert-rules.component').then((m) => m.AlertRulesComponent),
  },
  {
    path: 'orchestrator/environments',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./orchestrator/environments/environments.component').then((m) => m.EnvironmentsComponent),
  },
  { path: '**', redirectTo: '' },
];
