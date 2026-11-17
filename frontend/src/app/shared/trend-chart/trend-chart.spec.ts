import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TrendChart, TrendSeries } from './trend-chart';

const DATES = ['2026-11-13', '2026-11-14', '2026-11-15', '2026-11-16', '2026-11-17'];

const created: TrendSeries = {
  name: 'Created',
  color: 'var(--viz-series-1)',
  points: DATES.map((date, i) => ({ date, value: [2, 0, 3, 1, 5][i] })),
};

const resolved: TrendSeries = {
  name: 'Resolved',
  color: 'var(--viz-series-2)',
  points: DATES.map((date, i) => ({ date, value: [1, 1, 0, 2, 2][i] })),
};

describe('TrendChart', () => {
  let fixture: ComponentFixture<TrendChart>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const svg = () => page().querySelector('svg') as SVGSVGElement;
  const chart = () => page().querySelector('.chart') as HTMLElement;
  const tooltip = () => page().querySelector('.tooltip') as HTMLElement | null;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(series: TrendSeries[]) {
    await TestBed.configureTestingModule({ imports: [TrendChart] }).compileComponents();
    fixture = TestBed.createComponent(TrendChart);
    fixture.componentRef.setInput('series', series);
    fixture.componentRef.setInput('ariaLabel', 'Incidents created and resolved per day');
    await settle();
  }

  /** jsdom has no layout, so say the drawing is 640 units wide and starts at the left edge. */
  function pretendLayout() {
    vi.spyOn(svg(), 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 640, height: 240 } as DOMRect);
  }

  async function hoverAt(clientX: number) {
    pretendLayout();
    svg().dispatchEvent(new MouseEvent('pointermove', { clientX }));
    await settle();
  }

  async function press(key: string) {
    chart().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    await settle();
  }

  it('names each line in a legend when there are two', async () => {
    await setUp([created, resolved]);

    const legend = page().querySelector('.legend')!;
    expect(legend.textContent).toContain('Created');
    expect(legend.textContent).toContain('Resolved');
  });

  it('has no legend for a single line, since the title already says what it is', async () => {
    await setUp([created]);

    expect(page().querySelector('.legend')).toBeNull();
  });

  it('draws one line per series, in the colour of that series', async () => {
    await setUp([created, resolved]);

    const paths = Array.from(page().querySelectorAll('path.line'));
    expect(paths.length).toBe(2);
    expect(paths[0].getAttribute('stroke')).toBe('var(--viz-series-1)');
    expect(paths[1].getAttribute('stroke')).toBe('var(--viz-series-2)');
  });

  it('marks the latest day of each line with a dot', async () => {
    await setUp([created, resolved]);

    expect(page().querySelectorAll('circle.dot').length).toBe(2);
  });

  it('labels the axis with whole numbers that reach the highest value', async () => {
    await setUp([created, resolved]);

    const labels = Array.from(page().querySelectorAll('text.axis-text'))
      .map((t) => t.textContent!.trim())
      .filter((t) => /^\d+$/.test(t))
      .map(Number);

    expect(Math.max(...labels)).toBeGreaterThanOrEqual(5);
    expect(labels).toContain(0);
  });

  it('shows the first and last dates along the bottom', async () => {
    await setUp([created, resolved]);

    const bottom = Array.from(page().querySelectorAll('text.axis-text')).map((t) => t.textContent!.trim());
    expect(bottom.some((t) => t.includes('13'))).toBe(true);
    expect(bottom.some((t) => t.includes('17'))).toBe(true);
  });

  describe('hover', () => {
    it('shows nothing until the pointer is over the chart', async () => {
      await setUp([created, resolved]);

      expect(tooltip()).toBeNull();
    });

    it('shows each line\'s value for the day under the pointer', async () => {
      await setUp([created, resolved]);

      await hoverAt(330); // the middle of five days

      expect(tooltip()!.textContent).toContain('15');
      expect(tooltip()!.textContent).toContain('Created: 3');
      expect(tooltip()!.textContent).toContain('Resolved: 0');
    });

    it('marks the hovered day with a line and a dot on each series', async () => {
      await setUp([created, resolved]);

      await hoverAt(330);

      expect(page().querySelector('line.crosshair')).not.toBeNull();
      expect(page().querySelectorAll('circle.dot').length).toBe(4); // two end dots + two hover dots
    });

    it('snaps to the nearest day, and stays inside the first and last days', async () => {
      await setUp([created, resolved]);

      await hoverAt(0);
      expect(tooltip()!.textContent).toContain('Created: 2');

      await hoverAt(639);
      expect(tooltip()!.textContent).toContain('Created: 5');
    });

    it('goes away when the pointer leaves', async () => {
      await setUp([created, resolved]);
      await hoverAt(330);

      svg().dispatchEvent(new MouseEvent('pointerleave'));
      await settle();

      expect(tooltip()).toBeNull();
    });
  });

  describe('keyboard', () => {
    it('lands on the most recent day when the chart gets focus', async () => {
      await setUp([created, resolved]);

      chart().dispatchEvent(new FocusEvent('focus'));
      await settle();

      expect(tooltip()!.textContent).toContain('Created: 5');
    });

    it('moves a day at a time with the arrow keys, and stops at the ends', async () => {
      await setUp([created, resolved]);
      chart().dispatchEvent(new FocusEvent('focus'));
      await settle();

      await press('ArrowLeft');
      expect(tooltip()!.textContent).toContain('Created: 1');

      await press('ArrowRight');
      await press('ArrowRight'); // already on the last day
      expect(tooltip()!.textContent).toContain('Created: 5');
    });

    it('jumps to the first and last day with Home and End', async () => {
      await setUp([created, resolved]);
      chart().dispatchEvent(new FocusEvent('focus'));
      await settle();

      await press('Home');
      expect(tooltip()!.textContent).toContain('Created: 2');

      await press('End');
      expect(tooltip()!.textContent).toContain('Created: 5');
    });

    it('hides the tooltip on Escape and when focus leaves', async () => {
      await setUp([created, resolved]);
      chart().dispatchEvent(new FocusEvent('focus'));
      await settle();

      await press('Escape');
      expect(tooltip()).toBeNull();

      chart().dispatchEvent(new FocusEvent('focus'));
      await settle();
      chart().dispatchEvent(new FocusEvent('blur'));
      await settle();
      expect(tooltip()).toBeNull();
    });

    it('can be reached with the Tab key and says how to use it', async () => {
      await setUp([created, resolved]);

      expect(chart().getAttribute('tabindex')).toBe('0');
      expect(chart().getAttribute('aria-label')).toContain('arrow keys');
    });
  });

  describe('table view', () => {
    it('offers the same numbers as a table, so no value depends on a tooltip or on colour', async () => {
      await setUp([created, resolved]);

      (page().querySelector('.switch') as HTMLButtonElement).click();
      await settle();

      const headers = Array.from(page().querySelectorAll('thead th')).map((h) => h.textContent!.trim());
      expect(headers).toEqual(['Date', 'Created', 'Resolved']);

      const rows = page().querySelectorAll('tbody tr');
      expect(rows.length).toBe(5);
      const lastRow = Array.from(rows[4].querySelectorAll('th, td')).map((c) => c.textContent!.trim());
      expect(lastRow.slice(1)).toEqual(['5', '2']);
      expect(page().querySelector('svg')).toBeNull();
    });

    it('goes back to the chart', async () => {
      await setUp([created, resolved]);
      const toggle = () => page().querySelector('.switch') as HTMLButtonElement;

      toggle().click();
      await settle();
      expect(toggle().textContent).toContain('View as chart');

      toggle().click();
      await settle();
      expect(page().querySelector('svg')).not.toBeNull();
      expect(toggle().textContent).toContain('View as table');
    });
  });

  it('copes with a period that has no data at all', async () => {
    const empty = DATES.map((date) => ({ date, value: 0 }));
    await setUp([
      { ...created, points: empty },
      { ...resolved, points: empty },
    ]);

    expect(page().querySelectorAll('path.line').length).toBe(2);
    expect(text()).toContain('Created');
  });

  it('copes with no series at all', async () => {
    await setUp([]);

    expect(page().querySelectorAll('path.line').length).toBe(0);
  });

  describe('a percentage line with gaps', () => {
    const percent = (value: number) => `${value}%`;

    const compliance: TrendSeries = {
      name: 'Resolved within SLA',
      color: 'var(--viz-series-1)',
      points: [
        { date: DATES[0], value: null },
        { date: DATES[1], value: 100, note: '2 resolved in the last 7 days' },
        { date: DATES[2], value: 50 },
        { date: DATES[3], value: null },
        { date: DATES[4], value: 75 },
      ],
    };

    async function setUpPercent(series: TrendSeries[] = [compliance]) {
      await TestBed.configureTestingModule({ imports: [TrendChart] }).compileComponents();
      fixture = TestBed.createComponent(TrendChart);
      fixture.componentRef.setInput('series', series);
      fixture.componentRef.setInput('ariaLabel', 'Share that met their SLA');
      fixture.componentRef.setInput('maxValue', 100);
      fixture.componentRef.setInput('valueFormat', percent);
      await settle();
    }

    it('uses a fixed 0 to 100 axis, written as percentages', async () => {
      await setUpPercent();

      const labels = Array.from(page().querySelectorAll('svg text.axis-text')).map((n) => n.textContent!.trim());
      expect(labels).toEqual(expect.arrayContaining(['0%', '25%', '50%', '75%', '100%']));
    });

    it('breaks the line at a day with no data instead of dropping to zero', async () => {
      await setUpPercent();

      const path = page().querySelector('path.line')!.getAttribute('d')!;
      expect(path.match(/M/g)!.length).toBe(1); // one stretch: days 2 and 3
      expect(path).not.toContain('NaN');
    });

    it('shows a day that stands alone between gaps as a dot, since there is no line to sit on', async () => {
      await setUpPercent();

      // The last day (75%) is alone after a gap, and the end marker covers it; the lone-day dot is drawn too.
      expect(page().querySelectorAll('circle.dot').length).toBeGreaterThanOrEqual(2);
    });

    it('has no legend for a single line (the card names it)', async () => {
      await setUpPercent();

      expect(page().querySelector('.legend')).toBeNull();
    });

    it('says "no data" in the tooltip for a day without data, and draws no dot on it', async () => {
      await setUpPercent();
      page().querySelector('.chart')!.dispatchEvent(new Event('focus'));
      await settle();

      // Focus lands on the latest day (75%). Step left to the gap day.
      page().querySelector('.chart')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
      await settle();

      expect(page().querySelector('.tooltip')!.textContent).toContain('no data');
      // Only the end marker and the lone-day dot: nothing is drawn for the hovered gap day.
      expect(page().querySelectorAll('svg circle.dot').length).toBe(2);
    });

    it('writes a hovered day as a percentage with its note', async () => {
      await setUpPercent();
      page().querySelector('.chart')!.dispatchEvent(new Event('focus'));
      for (let i = 0; i < 3; i++) {
        page().querySelector('.chart')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
      }
      await settle();

      expect(page().querySelector('.tooltip')!.textContent).toContain('100% · 2 resolved in the last 7 days');
    });

    it('lists the same values in the table, with a dash for a day without data', async () => {
      await setUpPercent();

      page().querySelector<HTMLButtonElement>('button.switch')!.click();
      await settle();

      const cells = Array.from(page().querySelectorAll('tbody tr')).map((r) => r.querySelectorAll('td')[0].textContent!.trim());
      expect(cells).toEqual(['no data', '100% · 2 resolved in the last 7 days', '50%', 'no data', '75%']);
    });

    it('copes with a period where every day is a gap', async () => {
      await setUpPercent([{ ...compliance, points: DATES.map((date) => ({ date, value: null })) }]);

      expect(page().querySelector('path.line')!.getAttribute('d')).toBe('');
      expect(page().querySelectorAll('svg circle.dot').length).toBe(0);
    });
  });
});
