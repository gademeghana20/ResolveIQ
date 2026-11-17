import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'resolveiq.theme';

/**
 * The person's choice of colour theme: Light, Dark, or System (follow the computer, the default).
 * The choice is remembered in the browser. It works by setting data-theme on <html>; the styles do the rest.
 * (index.html applies the saved choice even earlier, before this runs, to avoid a flash of the wrong colours.)
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly preference = signal<ThemePreference>(this.read());

  constructor() {
    this.apply(this.preference());
  }

  set(preference: ThemePreference): void {
    this.preference.set(preference);
    this.apply(preference);
    this.write(preference);
  }

  private apply(preference: ThemePreference): void {
    const root = this.document.documentElement;

    if (preference === 'system') {
      root.removeAttribute('data-theme'); // no choice made: follow the computer
    } else {
      root.setAttribute('data-theme', preference);
    }
  }

  // Browsers can block storage (private windows, strict settings), so never assume it works.
  private read(): ThemePreference {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      return saved === 'light' || saved === 'dark' ? saved : 'system';
    } catch {
      return 'system';
    }
  }

  private write(preference: ThemePreference): void {
    try {
      if (preference === 'system') {
        localStorage.removeItem(STORAGE_KEY);
      } else {
        localStorage.setItem(STORAGE_KEY, preference);
      }
    } catch {
      // The choice still applies for this visit, it just will not be remembered.
    }
  }
}
