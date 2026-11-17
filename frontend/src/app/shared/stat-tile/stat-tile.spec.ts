import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { StatTile } from './stat-tile';

describe('StatTile', () => {
  let fixture: ComponentFixture<StatTile>;

  const page = () => fixture.nativeElement as HTMLElement;

  async function setUp(inputs: Record<string, unknown>) {
    await TestBed.configureTestingModule({ imports: [StatTile], providers: [provideRouter([])] }).compileComponents();
    fixture = TestBed.createComponent(StatTile);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('shows the label and the value', async () => {
    await setUp({ label: 'Unassigned', value: 7 });

    expect(page().textContent).toContain('Unassigned');
    expect(page().textContent).toContain('7');
  });

  it('shows a note under the value when there is one', async () => {
    await setUp({ label: 'Unassigned', value: 7, note: 'oldest has waited 3 h' });

    expect(page().textContent).toContain('oldest has waited 3 h');
  });

  it('is a plain tile, not a link, when it leads nowhere', async () => {
    await setUp({ label: 'Created', value: 4 });

    expect(page().querySelector('a')).toBeNull();
  });

  it('becomes a real link when given a destination, with its filter in the address', async () => {
    await setUp({ label: 'SLA breached', value: 3, link: ['/incidents'], queryParams: { slaState: 'Breached' } });

    const link = page().querySelector('a')!;
    expect(link.getAttribute('href')).toBe('/incidents?slaState=Breached');
    expect(link.textContent).toContain('SLA breached');
  });

  it('can be the big lead figure', async () => {
    await setUp({ label: 'Active incidents', value: 12, hero: true });

    expect(page().querySelector('.value')!.classList).toContain('hero');
  });

  it('is not a hero unless asked', async () => {
    await setUp({ label: 'Created', value: 4 });

    expect(page().querySelector('.value')!.classList).not.toContain('hero');
  });

  it('shows a text value such as a time as it is', async () => {
    await setUp({ label: 'Average time to resolve', value: '4 h 30 min' });

    expect(page().textContent).toContain('4 h 30 min');
  });
});
