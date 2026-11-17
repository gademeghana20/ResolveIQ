import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BarItem, BarList } from './bar-list';

describe('BarList', () => {
  let fixture: ComponentFixture<BarList>;

  const page = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(page().querySelectorAll('li.row'));
  const widthOf = (row: Element) => parseFloat((row.querySelector('.bar') as HTMLElement).style.width);

  async function setUp(items: BarItem[], extra: Record<string, unknown> = {}) {
    await TestBed.configureTestingModule({ imports: [BarList] }).compileComponents();
    fixture = TestBed.createComponent(BarList);
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('ariaLabel', 'Incidents by status');
    for (const [name, value] of Object.entries(extra)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('shows a row for each item, with the name and the value written out', async () => {
    await setUp([
      { label: 'Open', value: 6 },
      { label: 'Closed', value: 2 },
    ]);

    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('Open');
    expect(rows()[0].textContent).toContain('6');
    expect(rows()[1].textContent).toContain('Closed');
  });

  it('sizes bars in proportion, with the longest leaving room for its value', async () => {
    await setUp([
      { label: 'Big', value: 10 },
      { label: 'Half', value: 5 },
    ]);

    expect(widthOf(rows()[0])).toBe(80);
    expect(widthOf(rows()[1])).toBe(40);
  });

  it('keeps two close values visibly different', async () => {
    await setUp([
      { label: 'A', value: 100 },
      { label: 'B', value: 90 },
    ]);

    expect(widthOf(rows()[0])).toBeGreaterThan(widthOf(rows()[1]));
  });

  it('draws nothing for a zero, but still lists it', async () => {
    await setUp([
      { label: 'Open', value: 3 },
      { label: 'On hold', value: 0 },
    ]);

    expect(rows().length).toBe(2);
    expect(widthOf(rows()[1])).toBe(0);
    expect(rows()[1].textContent).toContain('0');
  });

  it('uses one colour for every bar unless an item says otherwise', async () => {
    await setUp([
      { label: 'Breached', value: 2, color: 'var(--viz-critical)' },
      { label: 'On track', value: 5 },
    ]);

    const colours = rows().map((row) => (row.querySelector('.bar') as HTMLElement).style.background);
    expect(colours[0]).toContain('--viz-critical');
    expect(colours[1]).toContain('--viz-series-1');
  });

  it('says there is nothing to show when every value is zero', async () => {
    await setUp([
      { label: 'Open', value: 0 },
      { label: 'Closed', value: 0 },
    ]);

    expect(rows().length).toBe(0);
    expect(page().textContent).toContain('Nothing to show for this period.');
  });

  it('says there is nothing to show for an empty list, using the words it is given', async () => {
    await setUp([], { emptyText: 'There are no incidents yet.' });

    expect(page().textContent).toContain('There are no incidents yet.');
  });

  it('prints a bar\'s own value text at its tip instead of the number, when it has one', async () => {
    await setUp([
      { label: 'Critical', value: 3.5, valueText: '3 h 30 min' },
      { label: 'Low', value: 0, valueText: '—' },
    ]);

    expect(rows()[0].querySelector('.value')!.textContent).toBe('3 h 30 min');
    expect(rows()[0].getAttribute('title')).toBe('Critical: 3 h 30 min');
    expect(rows()[1].querySelector('.value')!.textContent).toBe('—');
  });

  it('is named for screen readers', async () => {
    await setUp([{ label: 'Open', value: 1 }]);

    expect(page().querySelector('ul')!.getAttribute('aria-label')).toBe('Incidents by status');
  });
});
