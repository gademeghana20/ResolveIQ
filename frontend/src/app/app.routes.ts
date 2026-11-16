import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';
import { Login } from './features/auth/login/login';
import { IncidentList } from './features/incidents/incident-list/incident-list';

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: 'incidents', component: IncidentList, canActivate: [authGuard] },
  { path: '', pathMatch: 'full', redirectTo: 'incidents' },
  { path: '**', redirectTo: 'incidents' },
];
