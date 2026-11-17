import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';
import { roleGuard } from './core/auth/role.guard';

// Pages use loadComponent so their code is downloaded only when someone visits them (lazy loading).
// That keeps the first download small: the login page does not carry the incident table's code.
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login/login').then((m) => m.Login) },
  {
    path: 'forgot-password',
    loadComponent: () => import('./features/auth/forgot-password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./features/auth/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  { path: 'register', loadComponent: () => import('./features/auth/register/register').then((m) => m.Register) },

  // Everything signed-in people see lives inside the Shell (the sidebar frame) and behind the guard.
  // The Shell is lazy too, so the sign-in pages do not download the sidebar's code.
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell').then((m) => m.Shell),
    canActivate: [authGuard],
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'incidents',
        loadComponent: () =>
          import('./features/incidents/incident-list/incident-list').then((m) => m.IncidentList),
      },
      // "new" must come before ":id", or "new" would be read as an incident id.
      {
        path: 'incidents/new',
        loadComponent: () =>
          import('./features/incidents/incident-create/incident-create').then((m) => m.IncidentCreate),
      },
      {
        path: 'incidents/:id',
        loadComponent: () =>
          import('./features/incidents/incident-detail/incident-detail').then((m) => m.IncidentDetail),
      },
      {
        path: 'account/password',
        loadComponent: () => import('./features/account/change-password/change-password').then((m) => m.ChangePassword),
      },
      // Admin area: only people with the Admin role may open it.
      {
        path: 'admin',
        canActivate: [roleGuard('Admin')],
        loadComponent: () => import('./features/admin/admin-layout/admin-layout').then((m) => m.AdminLayout),
        children: [
          {
            path: 'users',
            loadComponent: () => import('./features/admin/users/users').then((m) => m.Users),
          },
          {
            path: 'categories',
            loadComponent: () => import('./features/admin/categories/categories').then((m) => m.Categories),
          },
          {
            path: 'sla',
            loadComponent: () => import('./features/admin/sla/sla-policies').then((m) => m.SlaPolicies),
          },
          { path: '', pathMatch: 'full', redirectTo: 'users' },
        ],
      },
      { path: '', pathMatch: 'full', redirectTo: 'incidents' },
    ],
  },

  { path: '**', redirectTo: 'incidents' },
];
