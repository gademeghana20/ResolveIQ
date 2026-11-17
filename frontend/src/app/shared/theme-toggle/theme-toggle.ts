import { Component, computed, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';

import { ThemePreference, ThemeService } from '../../core/theme/theme.service';

interface Choice {
  value: ThemePreference;
  label: string;
  icon: string;
}

const CHOICES: Choice[] = [
  { value: 'light', label: 'Light', icon: 'light_mode' },
  { value: 'dark', label: 'Dark', icon: 'dark_mode' },
  { value: 'system', label: 'Use my computer\'s setting', icon: 'brightness_auto' },
];

/** A small button that opens the Light / Dark / System choice. Used on the sidebar layout and the sign-in pages. */
@Component({
  selector: 'app-theme-toggle',
  imports: [MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    <button
      mat-icon-button
      type="button"
      class="toggle"
      [matMenuTriggerFor]="menu"
      [attr.aria-label]="'Colour theme: ' + current().label + '. Change it.'"
    >
      <mat-icon>{{ current().icon }}</mat-icon>
    </button>

    <mat-menu #menu="matMenu" xPosition="before">
      @for (choice of choices; track choice.value) {
        <button
          mat-menu-item
          type="button"
          role="menuitemradio"
          [attr.aria-checked]="theme.preference() === choice.value"
          (click)="theme.set(choice.value)"
        >
          <mat-icon>{{ choice.icon }}</mat-icon>
          <span>{{ choice.label }}</span>
          @if (theme.preference() === choice.value) {
            <mat-icon class="check" aria-hidden="true">check</mat-icon>
          }
        </button>
      }
    </mat-menu>
  `,
  styles: `
    .check {
      margin: 0 0 0 auto;
      color: var(--mat-sys-primary);
    }
  `,
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);
  protected readonly choices = CHOICES;

  /** The current choice, shown as the button's icon. */
  protected readonly current = computed(() => CHOICES.find((c) => c.value === this.theme.preference()) ?? CHOICES[2]);
}
