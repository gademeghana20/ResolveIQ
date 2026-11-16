import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';

// A temporary page so there is somewhere to land after login. It becomes the real incident list soon.
@Component({
  selector: 'app-incident-list',
  imports: [MatButtonModule],
  template: `
    <main style="padding: 24px">
      <h1>Incidents</h1>
      <p>Signed in as <strong>{{ auth.email() }}</strong> ({{ auth.roles().join(', ') }}).</p>
      <p>The incident list will appear here.</p>
      <button mat-stroked-button type="button" (click)="signOut()">Sign out</button>
    </main>
  `,
})
export class IncidentList {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected signOut(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }
}
