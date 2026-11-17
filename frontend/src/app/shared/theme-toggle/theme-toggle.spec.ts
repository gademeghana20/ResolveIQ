import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatMenuModule } from '@angular/material/menu';

import { ThemeService } from '../../core/theme/theme.service';
import { ThemeToggle } from './theme-toggle';

describe('ThemeToggle', () => {
  let fixture: ComponentFixture<ThemeToggle>;
  let theme: ThemeService;

  const page = () => fixture.nativeElement as HTMLElement;
  const trigger = () => page().querySelector('button.toggle') as HTMLButtonElement;
  const options = () => Array.from(document.querySelectorAll('button[mat-menu-item]')) as HTMLButtonElement[];
  const optionNamed = (name: string) => options().find((o) => o.textContent!.includes(name))!;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function openMenu() {
    trigger().click();
    await settle();
  }

  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');

    await TestBed.configureTestingModule({
      imports: [ThemeToggle, MatMenuModule],
      providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }],
    }).compileComponents();

    theme = TestBed.inject(ThemeService);
    fixture = TestBed.createComponent(ThemeToggle);
    await settle();
  });

  afterEach(() => document.documentElement.removeAttribute('data-theme'));

  it('says which theme is in use, in its label for screen readers', () => {
    expect(trigger().getAttribute('aria-label')).toContain('Colour theme');
    expect(trigger().getAttribute('aria-label')).toContain("Use my computer's setting");
  });

  it('offers Light, Dark, and the computer\'s own setting', async () => {
    await openMenu();

    expect(options().map((o) => o.textContent!)).toEqual([
      expect.stringContaining('Light'),
      expect.stringContaining('Dark'),
      expect.stringContaining("Use my computer's setting"),
    ]);
  });

  it('marks the current choice with a tick, as well as in its accessibility state', async () => {
    await openMenu();

    expect(optionNamed("Use my computer's setting").getAttribute('aria-checked')).toBe('true');
    expect(optionNamed('Light').getAttribute('aria-checked')).toBe('false');
    expect(optionNamed("Use my computer's setting").querySelector('.check')).not.toBeNull();
    expect(optionNamed('Dark').querySelector('.check')).toBeNull();
  });

  it('switches to dark when Dark is chosen', async () => {
    await openMenu();

    optionNamed('Dark').click();
    await settle();

    expect(theme.preference()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(trigger().getAttribute('aria-label')).toContain('Dark');
  });

  it('switches to light when Light is chosen', async () => {
    await openMenu();

    optionNamed('Light').click();
    await settle();

    expect(theme.preference()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('can go back to following the computer', async () => {
    theme.set('dark');
    await settle();
    await openMenu();

    optionNamed("Use my computer's setting").click();
    await settle();

    expect(theme.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('remembers the choice for next time', async () => {
    await openMenu();

    optionNamed('Dark').click();
    await settle();

    expect(localStorage.getItem('resolveiq.theme')).toBe('dark');
  });
});
