import { TestBed } from '@angular/core/testing';

import { AuthBrand } from './auth-brand';

describe('AuthBrand', () => {
  async function create() {
    await TestBed.configureTestingModule({ imports: [AuthBrand] }).compileComponents();
    const fixture = TestBed.createComponent(AuthBrand);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the logo and a headline', async () => {
    const panel = await create();

    expect(panel.querySelector('app-logo')).not.toBeNull();
    expect(panel.querySelector('.headline')!.textContent).toContain('From reported to resolved, on time.');
  });

  it('says in one sentence what the app is for', async () => {
    const panel = await create();

    const lead = panel.querySelector('.lead')!.textContent!;
    expect(lead).toContain('One place to report problems');
    expect(lead).toContain('every deadline');
  });

  it('lists five benefits, each with a title and an explanation', async () => {
    const panel = await create();

    const items = Array.from(panel.querySelectorAll('.points li'));
    expect(items.map((li) => li.querySelector('strong')!.textContent)).toEqual([
      'Report in seconds',
      'Never miss a deadline',
      'Know who is doing what',
      'Every change on record',
      'Roles that fit your team',
    ]);
    // An explanation sits under every title, so no benefit is just a slogan.
    expect(items.every((li) => (li.querySelector('span')!.textContent ?? '').length > 30)).toBe(true);
  });

  it('keeps every explanation short, so the panel stays calm and does not overwhelm the form', async () => {
    const panel = await create();

    const longest = Math.max(...Array.from(panel.querySelectorAll('.points span')).map((s) => (s.textContent ?? '').length));
    expect(longest).toBeLessThanOrEqual(70);
  });

  it('only claims things the app really does: the SLA clock, and what each role can do', async () => {
    const panel = await create();

    expect(panel.textContent).toContain('Clocks start at assignment, pause on hold, and warn you early');
    expect(panel.textContent).toContain('Employees report, managers assign, admins manage users');
  });

  it('no longer has the "Private by design" point', async () => {
    const panel = await create();

    expect(panel.textContent).not.toContain('Private by design');
  });

  it('keeps the decorative circles away from screen readers', async () => {
    const panel = await create();

    const halos = Array.from(panel.querySelectorAll('.halo'));
    expect(halos.length).toBe(2);
    expect(halos.every((halo) => halo.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('has no leftover ribbon shapes', async () => {
    const panel = await create();

    expect(panel.querySelectorAll('.ring').length).toBe(0);
  });
});
