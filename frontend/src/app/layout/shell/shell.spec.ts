import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { fakeToken } from '../../testing/fake-token';
import { Shell } from './shell';

describe('Shell', () => {
  let fixture: ComponentFixture<Shell>;

  const page = () => fixture.nativeElement as HTMLElement;
  const navLabels = () => Array.from(page().querySelectorAll('nav .link .label')).map((l) => l.textContent!.trim());

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(options: { role?: string; email?: string; narrow?: boolean } = {}) {
    const { role = 'Manager', email = 'manager@example.com', narrow = false } = options;

    localStorage.clear();
    localStorage.setItem('resolveiq.session', JSON.stringify({ token: fakeToken(), email, roles: [role] }));

    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: narrow, breakpoints: {} }) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Shell);
    await settle();
  }

  describe('the sidebar', () => {
    it('shows the ResolveIQ logo, which leads home to the dashboard', async () => {
      await setUp();

      const brand = page().querySelector('a.brand')!;
      expect(brand.querySelector('app-logo')).not.toBeNull();
      expect(brand.getAttribute('href')).toBe('/dashboard');
      expect(brand.getAttribute('aria-label')).toContain('ResolveIQ');
    });

    it('links to the dashboard and the incidents', async () => {
      await setUp();

      expect(navLabels()).toEqual(['Dashboard', 'Incidents']);
      const hrefs = Array.from(page().querySelectorAll('nav .link')).map((a) => a.getAttribute('href'));
      expect(hrefs).toEqual(['/dashboard', '/incidents']);
    });

    it('offers a Change password link, apart from the main navigation, for everyone', async () => {
      await setUp();

      const link = page().querySelector('a.account')!;
      expect(link.getAttribute('href')).toBe('/account/password');
      expect(link.textContent).toContain('Change password');
      expect(link.closest('nav')).toBeNull();
    });

    it('shows the Admin link to an Admin', async () => {
      await setUp({ role: 'Admin' });

      expect(navLabels()).toEqual(['Dashboard', 'Incidents', 'Admin']);
    });

    it.each(['Manager', 'Employee'])('does not show the Admin link to a %s', async (role) => {
      await setUp({ role });

      expect(navLabels()).not.toContain('Admin');
    });

    it('has a New incident button that is not one of the navigation links', async () => {
      await setUp();

      const button = page().querySelector('a.new-incident')!;
      expect(button.getAttribute('href')).toBe('/incidents/new');
      expect(button.textContent).toContain('New incident');
      expect(page().querySelector('nav')!.contains(button)).toBe(false);
    });

    it('names its navigation for screen readers', async () => {
      await setUp();

      expect(page().querySelector('nav')!.getAttribute('aria-label')).toBe('Main');
    });
  });

  describe('logging out', () => {
    it('signs out and goes to the login page', async () => {
      await setUp();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

      (page().querySelector('button.logout') as HTMLButtonElement).click();

      expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
      expect(navigate).toHaveBeenCalledWith('/login');
    });

    it('is labelled "Log out"', async () => {
      await setUp();

      expect(page().querySelector('button.logout')!.textContent).toContain('Log out');
    });
  });

  describe('the top bar', () => {
    it('says who is signed in, and with which role', async () => {
      await setUp();

      expect(page().querySelector('.user .email')!.textContent).toContain('manager@example.com');
      expect(page().querySelector('.user .role')!.textContent).toContain('Manager');
    });

    it.each([
      ['manager@example.com', 'MA'],
      ['dana.lee@example.com', 'DL'],
      ['sam_k.o@example.com', 'SK'],
      ['x@example.com', 'X'],
    ])('shows %s as the initials %s in the avatar', async (email, initials) => {
      await setUp({ email });

      expect(page().querySelector('.avatar')!.textContent!.trim()).toBe(initials);
    });

    it('offers the colour theme switch', async () => {
      await setUp();

      expect(page().querySelector('app-theme-toggle')).not.toBeNull();
    });
  });

  describe('on a wide screen', () => {
    it('keeps the sidebar open and has no menu button', async () => {
      await setUp({ narrow: false });

      expect(page().querySelector('button.menu-button')).toBeNull();
      expect(page().querySelector('mat-sidenav')!.classList).toContain('mat-drawer-opened');
    });
  });

  describe('on a narrow screen', () => {
    it('hides the sidebar behind a menu button', async () => {
      await setUp({ narrow: true });

      expect(page().querySelector('button.menu-button')).not.toBeNull();
      expect(page().querySelector('mat-sidenav')!.classList).not.toContain('mat-drawer-opened');
    });

    it('opens the sidebar when the menu button is pressed', async () => {
      await setUp({ narrow: true });

      (page().querySelector('button.menu-button') as HTMLButtonElement).click();
      await settle();

      expect(page().querySelector('mat-sidenav')!.classList).toContain('mat-drawer-opened');
    });

    it('names the menu button for screen readers', async () => {
      await setUp({ narrow: true });

      expect(page().querySelector('button.menu-button')!.getAttribute('aria-label')).toBe('Open the menu');
    });
  });
});
