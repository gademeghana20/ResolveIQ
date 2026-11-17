import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { Logo } from '../../../shared/logo/logo';

interface Benefit {
  icon: string;
  title: string;
  text: string;
}

// Every claim here is something the app really does.
const BENEFITS: Benefit[] = [
  {
    icon: 'bolt',
    title: 'Report in seconds',
    text: 'Describe it once and your managers see it right away.',
  },
  {
    icon: 'timer',
    title: 'Never miss a deadline',
    text: 'Each incident gets a clock that starts when it is assigned, pauses when it is on hold, and flags it before time runs out.',
  },
  {
    icon: 'groups',
    title: 'Know who is doing what',
    text: 'Clear ownership, dashboards for the whole picture, and a workload view for managers.',
  },
  {
    icon: 'history',
    title: 'Every change on record',
    text: 'A complete history of who changed what, and when, so nothing is ever lost.',
  },
  {
    icon: 'lock',
    title: 'Private by design',
    text: 'Employees see only incidents they reported or work on.',
  },
];

/** The green panel beside the sign-in and register forms: the logo, a headline, and what the app gives you. */
@Component({
  selector: 'app-auth-brand',
  imports: [Logo, MatIconModule],
  template: `
    <app-logo class="logo" />

    <div class="message">
      <p class="headline">From reported to resolved, on time.</p>
      <p class="lead">
        ResolveIQ gives your team one place to report problems, hand them to the right person, and track every
        deadline until the job is done.
      </p>

      <ul class="points">
        @for (benefit of benefits; track benefit.title) {
          <li>
            <mat-icon aria-hidden="true">{{ benefit.icon }}</mat-icon>
            <div>
              <strong>{{ benefit.title }}</strong>
              <span>{{ benefit.text }}</span>
            </div>
          </li>
        }
      </ul>
    </div>

    <!-- Two big, faint circles that run off the edge of the panel. Purely decoration. -->
    <span class="halo halo-top" aria-hidden="true"></span>
    <span class="halo halo-bottom" aria-hidden="true"></span>
  `,
  styleUrl: './auth-brand.css',
})
export class AuthBrand {
  protected readonly benefits = BENEFITS;
}
