import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AdminLayout } from './admin-layout';

describe('AdminLayout', () => {
  it('offers the admin sections as links', async () => {
    await TestBed.configureTestingModule({ imports: [AdminLayout], providers: [provideRouter([])] }).compileComponents();
    const fixture = TestBed.createComponent(AdminLayout);
    fixture.detectChanges();
    await fixture.whenStable();

    const links = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('nav a'));
    expect(links.map((a) => a.textContent!.trim())).toEqual(['Users', 'Categories', 'SLA policies']);
    expect(links[0].getAttribute('href')).toContain('users');
    expect(links[1].getAttribute('href')).toContain('categories');
    expect(links[2].getAttribute('href')).toContain('sla');
  });

  it('names its navigation for screen readers', async () => {
    await TestBed.configureTestingModule({ imports: [AdminLayout], providers: [provideRouter([])] }).compileComponents();
    const fixture = TestBed.createComponent(AdminLayout);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('nav')!.getAttribute('aria-label')).toBe('Admin sections');
  });
});
