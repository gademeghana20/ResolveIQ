import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NotFound } from './not-found';

describe('NotFound', () => {
  async function render() {
    await TestBed.configureTestingModule({ imports: [NotFound], providers: [provideRouter([])] }).compileComponents();
    const fixture = TestBed.createComponent(NotFound);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('says the page does not exist, with a heading for screen readers', async () => {
    const page = await render();

    expect(page.querySelector('h1')!.textContent).toBe('Page not found');
    expect(page.textContent).toContain('does not exist');
  });

  it('offers ways back to the dashboard and the incidents', async () => {
    const page = await render();

    const links = Array.from(page.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(links).toEqual(['/dashboard', '/incidents']);
  });
});
