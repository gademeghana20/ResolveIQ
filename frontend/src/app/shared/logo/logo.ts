import { Component, input } from '@angular/core';

/**
 * The ResolveIQ mark and name: a check (a resolved incident) with an orange spark.
 * It takes its colour from the text colour around it, so it works on the green sidebar and on a light page.
 */
@Component({
  selector: 'app-logo',
  template: `
    <svg class="mark" viewBox="0 0 32 32" aria-hidden="true">
      <rect width="32" height="32" rx="9" fill="currentColor" opacity="0.22" />
      <path
        d="M9 17.2l4.6 4.6L23.2 11"
        fill="none"
        stroke="currentColor"
        stroke-width="3.2"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
      <circle cx="24" cy="8" r="2.4" fill="#f6a93b" />
    </svg>
    @if (showName()) {
      <span class="name">Resolve<span class="iq">IQ</span></span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 10px;
    }

    .mark {
      width: var(--logo-size, 32px);
      height: var(--logo-size, 32px);
      flex: none;
    }

    .name {
      font-size: calc(var(--logo-size, 32px) * 0.6);
      font-weight: 700;
      letter-spacing: -0.01em;
      line-height: 1;
    }

    .iq {
      font-weight: 400;
      opacity: 0.85;
    }
  `,
})
export class Logo {
  /** Hide the name to show just the mark (for a narrow space). */
  readonly showName = input(true);
}
