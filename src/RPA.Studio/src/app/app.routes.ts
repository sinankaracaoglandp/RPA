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
  { path: '**', redirectTo: '' },
];
