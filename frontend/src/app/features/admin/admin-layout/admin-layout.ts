import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/** The frame of the Admin area: a row of sections, and the current section below it. */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, MatButtonModule],
  template: `
    <nav class="sections" aria-label="Admin sections">
      <a mat-button routerLink="users" routerLinkActive="active" ariaCurrentWhenActive="page">Users</a>
      <a mat-button routerLink="categories" routerLinkActive="active" ariaCurrentWhenActive="page">Categories</a>
      <a mat-button routerLink="sla" routerLinkActive="active" ariaCurrentWhenActive="page">SLA policies</a>
      <a mat-button routerLink="audit" routerLinkActive="active" ariaCurrentWhenActive="page">Audit log</a>
    </nav>

    <router-outlet />
  `,
  styles: `
    .sections {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
      margin-bottom: 16px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      padding-bottom: 8px;
    }

    /* The section you are in is marked by weight as well as colour. */
    .active {
      font-weight: 700;
      background: var(--mat-sys-secondary-container);
    }
  `,
})
export class AdminLayout {}
