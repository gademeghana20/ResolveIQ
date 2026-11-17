import { TestBed } from '@angular/core/testing';

import { Logo } from './logo';

describe('Logo', () => {
  async function create(showName?: boolean) {
    await TestBed.configureTestingModule({ imports: [Logo] }).compileComponents();
    const fixture = TestBed.createComponent(Logo);
    if (showName !== undefined) {
      fixture.componentRef.setInput('showName', showName);
    }
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the mark and the name', async () => {
    const logo = await create();

    expect(logo.querySelector('svg')).not.toBeNull();
    expect(logo.textContent).toContain('ResolveIQ');
  });

  it('can show just the mark, for a narrow space', async () => {
    const logo = await create(false);

    expect(logo.querySelector('svg')).not.toBeNull();
    expect(logo.textContent).not.toContain('ResolveIQ');
  });

  it('hides the drawing from screen readers, since the name is the label', async () => {
    const logo = await create();

    expect(logo.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
  });
});
