import { Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

/**
 * A small coloured label such as "High" or "Breached". It is a shared component:
 * lists, detail pages and dashboards all reuse it instead of repeating the styling.
 * Colour is never the only signal; the text always says what it means.
 */
@Component({
  selector: 'app-badge',
  template: `<span class="badge" [class]="'badge tone-' + tone()">{{ label() }}</span>`,
  styleUrl: './badge.css',
})
export class Badge {
  readonly label = input.required<string>();
  readonly tone = input<BadgeTone>('neutral');
}
