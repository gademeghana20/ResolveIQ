import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo/logo';
import { ThemeToggle } from '../../shared/theme-toggle/theme-toggle';

/** Below this width the sidebar becomes a menu that slides over the page. */
const NARROW_SCREEN = '(max-width: 900px)';

/**
 * The frame around every signed-in page: the green sidebar (logo, navigation, log out),
 * a slim top bar (theme switch, who is signed in), and the current page.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatButtonModule,
    MatIconModule,
    Logo,
    ThemeToggle,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly narrow = toSignal(
    inject(BreakpointObserver)
      .observe(NARROW_SCREEN)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  /** Whether the slide-out menu is open (only used on narrow screens). */
  protected readonly menuOpen = signal(false);

  /** "dana.lee@example.com" -> "DL"; "manager@example.com" -> "MA". Shown in the little avatar. */
  protected readonly initials = computed(() => {
    const name = (this.auth.email() ?? '').split('@')[0];
    const parts = name.split(/[._\-+]+/).filter(Boolean);
    const letters = parts.length > 1 ? parts[0][0] + parts[1][0] : name.slice(0, 2);
    return letters.toUpperCase() || '?';
  });

  protected readonly roleText = computed(() => this.auth.roles().join(', '));

  constructor() {
    // After choosing a page from the slide-out menu, close it.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.menuOpen.set(false));
  }

  protected signOut(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }
}
