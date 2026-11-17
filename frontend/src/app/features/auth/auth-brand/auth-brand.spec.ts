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
    expect(lead).toContain('one place to report problems');
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
      'Private by design',
    ]);
    // An explanation sits under every title, so no benefit is just a slogan.
    expect(items.every((li) => (li.querySelector('span')!.textContent ?? '').length > 30)).toBe(true);
  });

  it('only claims things the app really does: the SLA clock and who can see what', async () => {
    const panel = await create();

    expect(panel.textContent).toContain('starts when it is assigned, pauses when it is on hold');
    expect(panel.textContent).toContain('Employees see only incidents they reported or work on');
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
