import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';
import { Shell } from './layout/shell/shell';

// Pages use loadComponent so their code is downloaded only when someone visits them (lazy loading).
// That keeps the first download small: the login page does not carry the incident table's code.
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login/login').then((m) => m.Login) },

  // Everything signed-in people see lives inside the Shell (the top bar) and behind the guard.
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      {
        path: 'incidents',
        loadComponent: () =>
          import('./features/incidents/incident-list/incident-list').then((m) => m.IncidentList),
      },
      { path: '', pathMatch: 'full', redirectTo: 'incidents' },
    ],
  },

  { path: '**', redirectTo: 'incidents' },
];
