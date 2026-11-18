import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

/** Shown for any address the app does not have, inside the normal frame so the menu is still there. */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButtonModule],
  template: `
    <section class="not-found" aria-labelledby="nf-title">
      <p class="code" aria-hidden="true">404</p>
      <h1 id="nf-title">Page not found</h1>
      <p>This page does not exist, or the link is out of date.</p>
      <div class="actions">
        <a mat-flat-button routerLink="/dashboard">Go to the dashboard</a>
        <a mat-stroked-button routerLink="/incidents">See incidents</a>
      </div>
    </section>
  `,
  styles: `
    .not-found {
      max-width: 480px;
      margin: 48px auto;
      text-align: center;
      color: var(--mat-sys-on-surface-variant);
    }
    .code {
      margin: 0;
      font: var(--mat-sys-display-large);
      color: var(--mat-sys-primary);
    }
    h1 {
      font: var(--mat-sys-headline-medium);
      color: var(--mat-sys-on-surface);
      margin: 0 0 8px;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      justify-content: center;
      margin-top: 24px;
    }
  `,
})
export class NotFound {}
