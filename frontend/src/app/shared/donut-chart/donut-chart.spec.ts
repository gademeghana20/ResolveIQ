import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DonutChart, DonutItem } from './donut-chart';

const ITEMS: DonutItem[] = [
  { label: 'Breached', value: 2, color: 'var(--viz-critical)' },
  { label: 'On track', value: 6, color: 'var(--viz-good)' },
  { label: 'Met', value: 12, color: 'var(--viz-series-1)', detail: 'resolved in time' },
];

describe('DonutChart', () => {
  let fixture: ComponentFixture<DonutChart>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const slices = () => Array.from(page().querySelectorAll<SVGCircleElement>('circle.slice'));
  const legendRows = () => Array.from(page().querySelectorAll('li.item'));
  const ring = () => page().querySelector('.ring') as HTMLElement;
  const readout = () => (page().querySelector('.readout')!.textContent ?? '').replace(/\s+/g, ' ').trim();

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(items: DonutItem[] = ITEMS, extra: Record<string, unknown> = {}) {
    await TestBed.configureTestingModule({ imports: [DonutChart] }).compileComponents();
    fixture = TestBed.createComponent(DonutChart);
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('ariaLabel', 'Incidents by SLA state');
    for (const [name, value] of Object.entries(extra)) {
      fixture.componentRef.setInput(name, value);
    }
    await settle();
  }

  async function press(key: string) {
    ring().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    await settle();
  }

  it('shows the total in the middle, with what it counts', async () => {
    await setUp(ITEMS, { centerLabel: 'incidents' });

    expect(readout()).toBe('20 incidents');
  });

  it('draws one arc per part that has something in it, in the part\'s own colour', async () => {
    await setUp();

    expect(slices().length).toBe(3);
    expect(slices().map((s) => s.getAttribute('stroke'))).toEqual(['var(--viz-critical)', 'var(--viz-good)', 'var(--viz-series-1)']);
  });

  it('leaves a part with nothing in it off the ring, but keeps it in the legend with its zero', async () => {
    await setUp([...ITEMS, { label: 'Paused', value: 0, color: 'var(--viz-neutral)' }]);

    expect(slices().length).toBe(3);
    expect(legendRows().length).toBe(4);
    expect(legendRows()[3].textContent).toContain('Paused');
    expect(legendRows()[3].textContent).toContain('0%');
  });

  it('writes every part out in a legend: name, count and share', async () => {
    await setUp();

    expect(legendRows().map((r) => r.querySelector('.label')!.textContent!.trim())).toEqual(['Breached', 'On track', 'Met']);
    expect(legendRows().map((r) => r.querySelector('.count')!.textContent!.trim())).toEqual(['2', '6', '12']);
    expect(legendRows().map((r) => r.querySelector('.share')!.textContent!.trim())).toEqual(['10%', '30%', '60%']);
  });

  it('shows the small print under a part\'s name when it has some', async () => {
    await setUp();

    expect(legendRows()[2].querySelector('.detail')!.textContent).toContain('resolved in time');
    expect(legendRows()[0].querySelector('.detail')).toBeNull();
  });

  it('separates the parts with small gaps, so neighbours never merge', async () => {
    await setUp();

    const [first] = slices();
    const arc = parseFloat(first.getAttribute('stroke-dasharray')!.split(' ')[0]);
    const circumference = 2 * Math.PI * 70;
    expect(arc).toBeLessThan((2 / 20) * circumference); // a tenth of the ring, less the gap
  });

  it('draws a single part as a whole ring, with no gap', async () => {
    await setUp([{ label: 'Open', value: 4, color: 'var(--viz-series-1)' }]);

    const arc = parseFloat(slices()[0].getAttribute('stroke-dasharray')!.split(' ')[0]);
    expect(arc).toBeCloseTo(2 * Math.PI * 70, 1);
  });

  it('puts the part being pointed at in the middle, and steps the others back', async () => {
    await setUp();

    slices()[1].dispatchEvent(new MouseEvent('pointerenter'));
    await settle();

    expect(readout()).toBe('6 On track · 30%');
    expect(slices()[1].classList).not.toContain('dim');
    expect(slices()[0].classList).toContain('dim');

    slices()[1].dispatchEvent(new MouseEvent('pointerleave'));
    await settle();

    expect(readout()).toBe('20 total');
    expect(slices().some((s) => s.classList.contains('dim'))).toBe(false);
  });

  it('does the same when a legend row is pointed at', async () => {
    await setUp();

    legendRows()[2].dispatchEvent(new MouseEvent('pointerenter'));
    await settle();

    expect(readout()).toContain('12');
    expect(readout()).toContain('Met · 60%');
  });

  describe('with the keyboard', () => {
    it('can be focused, and is named for screen readers', async () => {
      await setUp();

      expect(ring().getAttribute('tabindex')).toBe('0');
      expect(ring().getAttribute('aria-label')).toContain('Incidents by SLA state');
      expect(ring().getAttribute('aria-label')).toContain('arrow keys');
    });

    it('starts on the first part, then steps with the arrow keys, wrapping round', async () => {
      await setUp();

      ring().dispatchEvent(new Event('focus'));
      await settle();
      expect(readout()).toContain('Breached');

      await press('ArrowRight');
      expect(readout()).toContain('On track');
      await press('ArrowRight');
      await press('ArrowRight');
      expect(readout()).toContain('Breached'); // wrapped round
      await press('ArrowLeft');
      expect(readout()).toContain('Met');
    });

    it('jumps with Home and End, and lets go with Escape', async () => {
      await setUp();
      ring().dispatchEvent(new Event('focus'));

      await press('End');
      expect(readout()).toContain('Met');
      await press('Home');
      expect(readout()).toContain('Breached');
      await press('Escape');
      expect(readout()).toBe('20 total');
    });

    it('skips a part that is not on the ring', async () => {
      await setUp([
        { label: 'A', value: 3, color: 'red' },
        { label: 'Zero', value: 0, color: 'grey' },
        { label: 'B', value: 1, color: 'blue' },
      ]);
      ring().dispatchEvent(new Event('focus'));
      await settle();

      await press('ArrowRight');

      expect(readout()).toContain('B');
    });
  });

  describe('as a table', () => {
    it('is offered, and lists every part in plain text', async () => {
      await setUp();

      page().querySelector<HTMLButtonElement>('button.switch')!.click();
      await settle();

      const rows = Array.from(page().querySelectorAll('tbody tr')).map((r) =>
        Array.from(r.querySelectorAll('th, td')).map((c) => c.textContent!.trim()),
      );
      expect(rows).toEqual([
        ['Breached', '2', '10%'],
        ['On track', '6', '30%'],
        ['Met', '12', '60%'],
      ]);
      expect(page().querySelector('svg')).toBeNull();
      expect(page().querySelector('caption')!.textContent).toContain('Incidents by SLA state');
    });

    it('can be switched back to the chart', async () => {
      await setUp();
      const button = () => page().querySelector<HTMLButtonElement>('button.switch')!;

      button().click();
      await settle();
      expect(button().textContent).toContain('View as chart');

      button().click();
      await settle();
      expect(page().querySelector('svg')).not.toBeNull();
    });
  });

  describe('with nothing to show', () => {
    it('says so, instead of drawing an empty ring', async () => {
      await setUp(ITEMS.map((item) => ({ ...item, value: 0 })), { emptyText: 'There are no incidents yet.' });

      expect(text()).toContain('There are no incidents yet.');
      expect(page().querySelector('svg')).toBeNull();
      expect(page().querySelector('button.switch')).toBeNull();
    });

    it('copes with no parts at all', async () => {
      await setUp([]);

      expect(page().querySelector('svg')).toBeNull();
    });
  });
});
