import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { Logo } from '../../../shared/logo/logo';

/** The green panel beside the sign-in and register forms: the logo, a headline, and three reasons to use the app. */
@Component({
  selector: 'app-auth-brand',
  imports: [Logo, MatIconModule],
  template: `
    <app-logo class="logo" />

    <div class="message">
      <p class="headline">Resolve incidents faster.</p>
      <ul class="points">
        <li><mat-icon aria-hidden="true">bolt</mat-icon>Report a problem in seconds</li>
        <li><mat-icon aria-hidden="true">timer</mat-icon>Every incident gets a deadline you can see</li>
        <li><mat-icon aria-hidden="true">history</mat-icon>A full history of who did what, and when</li>
      </ul>
    </div>

    <!-- Two soft rings, a nod to the dashboard's charts. Purely decoration. -->
    <span class="ring ring-orange" aria-hidden="true"></span>
    <span class="ring ring-purple" aria-hidden="true"></span>
  `,
  styleUrl: './auth-brand.css',
})
export class AuthBrand {}
