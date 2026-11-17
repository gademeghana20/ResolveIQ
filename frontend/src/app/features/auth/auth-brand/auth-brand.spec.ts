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
    expect(panel.textContent).toContain('Resolve incidents faster.');
  });

  it('gives three reasons to use the app', async () => {
    const panel = await create();

    const points = Array.from(panel.querySelectorAll('.points li')).map((li) => li.textContent!);
    expect(points.length).toBe(3);
    expect(points[0]).toContain('Report a problem in seconds');
    expect(points[1]).toContain('deadline');
    expect(points[2]).toContain('history');
  });

  it('keeps the decorative rings away from screen readers', async () => {
    const panel = await create();

    const rings = Array.from(panel.querySelectorAll('.ring'));
    expect(rings.length).toBe(2);
    expect(rings.every((ring) => ring.getAttribute('aria-hidden') === 'true')).toBe(true);
  });
});
