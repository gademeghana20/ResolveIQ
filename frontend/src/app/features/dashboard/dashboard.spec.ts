import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatButtonToggleGroup } from '@angular/material/button-toggle';
import { provideRouter } from '@angular/router';

import { API_URL } from '../../core/api-config';
import { drawGrid, gridHeadings, gridRows } from '../../shared/data-grid/grid-testing';
import { DashboardStatistics, DashboardSummary } from './dashboard.models';
import { Dashboard } from './dashboard';

const summary: DashboardSummary = {
  totalIncidents: 30,
  activeIncidents: 12,
  assignedToMe: 3,
  unassigned: 4,
  oldestUnassignedWaitingMinutes: 200,
  sla: { notStarted: 4, onTrack: 5, atRisk: 2, paused: 1, breached: 3, met: 14, none: 1 },
  openAgeBuckets: [
    { name: 'Under1Day', count: 4 },
    { name: '1To3Days', count: 3 },
    { name: '3To7Days', count: 3 },
    { name: 'Over7Days', count: 2 },
  ],
};

const statistics: DashboardStatistics = {
  days: 30,
  from: '2026-10-19',
  to: '2026-11-17',
  byStatus: [
    { name: 'Open', count: 6 },
    { name: 'InProgress', count: 4 },
    { name: 'OnHold', count: 2 },
    { name: 'Resolved', count: 8 },
    { name: 'Closed', count: 10 },
  ],
  byPriority: [
    { name: 'Low', count: 2 },
    { name: 'Medium', count: 5 },
    { name: 'High', count: 9 },
    { name: 'Critical', count: 1 },
    { name: 'Unprioritized', count: 13 },
  ],
  byCategory: [
    { name: 'Network', count: 3 },
    { name: 'Hardware', count: 12 },
    { name: 'Uncategorized', count: 5 },
  ],
  createdPerDay: [
    { date: '2026-11-16', count: 2 },
    { date: '2026-11-17', count: 3 },
  ],
  resolvedPerDay: [
    { date: '2026-11-16', count: 1 },
    { date: '2026-11-17', count: 4 },
  ],
  averageResolutionHours: 4.5,
  slaCompliancePercent: 83.3,
  slaComplianceTrend: [
    { date: '2026-11-16', percent: null, resolved: 0 },
    { date: '2026-11-17', percent: 83.3, resolved: 6 },
  ],
  resolutionByPriority: [
    { priority: 'Critical', averageHours: 3.5, count: 2 },
    { priority: 'High', averageHours: 6, count: 4 },
    { priority: 'Medium', averageHours: 20.5, count: 3 },
    { priority: 'Low', averageHours: null, count: 0 },
  ],
  workload: [
    { userId: 'u1', email: 'dana@example.com', activeIncidents: 5, breachedIncidents: 1 },
    { userId: 'u2', email: 'lee@example.com', activeIncidents: 2, breachedIncidents: 0 },
  ],
};

describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let http: HttpTestingController;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const summaryRequest = () => http.expectOne(`${API_URL}/dashboard/summary`);
  const statsRequest = (days = 30): TestRequest =>
    http.expectOne((r) => r.url === `${API_URL}/dashboard/incident-statistics` && r.params.get('days') === String(days));
  const tile = (label: string) =>
    Array.from(page().querySelectorAll('app-stat-tile')).find((t) => t.textContent!.includes(label)) as HTMLElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp() {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Dashboard);
    await settle();
  }

  async function loadBoth(stats: DashboardStatistics = statistics) {
    await setUp();
    summaryRequest().flush(summary);
    statsRequest().flush(stats);
    await settle();
    await drawGrid(() => fixture.detectChanges());
  }

  it('asks for the current numbers, and for the last 30 days', async () => {
    await setUp();

    summaryRequest();
    statsRequest(30);
  });

  describe('right now', () => {
    it('leads with the active incidents as the hero figure', async () => {
      await loadBoth();

      const hero = tile('Active incidents');
      expect(hero.textContent).toContain('12');
      expect(hero.textContent).toContain('of 30 in total');
      expect(hero.querySelector('.value')!.classList).toContain('hero');
    });

    it('shows what is assigned to me', async () => {
      await loadBoth();

      expect(tile('Assigned to me').textContent).toContain('3');
    });

    it('says how long the oldest unassigned incident has waited', async () => {
      await loadBoth();

      expect(tile('Unassigned').textContent).toContain('4');
      expect(tile('Unassigned').textContent).toContain('oldest has waited 3 h 20 min');
    });

    it('says nothing about waiting when nothing is unassigned', async () => {
      await setUp();
      summaryRequest().flush({ ...summary, unassigned: 0, oldestUnassignedWaitingMinutes: null });
      statsRequest().flush(statistics);
      await settle();

      expect(tile('Unassigned').textContent).not.toContain('waited');
    });

    it('opens the matching incidents when Unassigned, At risk or Breached is clicked', async () => {
      await loadBoth();

      expect(tile('Unassigned').querySelector('a')!.getAttribute('href')).toBe('/incidents?scope=unassigned');
      expect(tile('SLA at risk').querySelector('a')!.getAttribute('href')).toBe('/incidents?slaState=AtRisk');
      expect(tile('SLA breached').querySelector('a')!.getAttribute('href')).toBe('/incidents?slaState=Breached');
    });

    it('does not link tiles whose number the list could not reproduce', async () => {
      await loadBoth();

      expect(tile('Active incidents').querySelector('a')).toBeNull();
      expect(tile('Assigned to me').querySelector('a')).toBeNull();
    });

    describe('the SLA state donut', () => {
      const card = () => Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('Incidents by SLA state'))!;
      const legend = () => Array.from(card().querySelectorAll('li.item'));
      const rowFor = (name: string) => legend().find((r) => r.querySelector('.label')!.textContent!.trim() === name)!;

      it('shows the total in the middle and every SLA state in words, with its count', async () => {
        await loadBoth();

        expect(card().querySelector('.readout')!.textContent).toContain('30');
        for (const [name, count] of [
          ['Breached', '3'],
          ['At risk', '2'],
          ['On track', '5'],
          ['Met', '14'],
          ['No clock running', '6'],
        ]) {
          expect(rowFor(name), name).toBeDefined();
          expect(rowFor(name).querySelector('.count')!.textContent).toBe(count);
        }
      });

      it('says what the combined "no clock running" part is made of', async () => {
        await loadBoth();

        const detail = rowFor('No clock running').querySelector('.detail')!.textContent!;
        expect(detail).toContain('Paused 1');
        expect(detail).toContain('Not started 4');
        expect(detail).toContain('No SLA 1');
      });

      it('colours breached, at risk and on track by what they mean', async () => {
        await loadBoth();

        const colourOf = (name: string) => (rowFor(name).querySelector('.key') as HTMLElement).style.background;

        expect(colourOf('Breached')).toContain('--viz-critical');
        expect(colourOf('At risk')).toContain('--viz-warning');
        expect(colourOf('On track')).toContain('--viz-good');
      });

      it('can be read as a table', async () => {
        await loadBoth();

        card().querySelector<HTMLButtonElement>('button.switch')!.click();
        await settle();

        expect(card().querySelectorAll('tbody tr').length).toBe(5);
      });

      it('says so when there are no incidents at all', async () => {
        await setUp();
        const none = { notStarted: 0, onTrack: 0, atRisk: 0, paused: 0, breached: 0, met: 0, none: 0 };
        summaryRequest().flush({ ...summary, totalIncidents: 0, activeIncidents: 0, sla: none });
        statsRequest().flush(statistics);
        await settle();

        expect(card().textContent).toContain('There are no incidents yet.');
      });
    });

    describe('how long open incidents have waited', () => {
      const rows = () => {
        const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('How long open incidents have waited'))!;
        return Array.from(card.querySelectorAll('li.row')).map((r) => [r.querySelector('.name')!.textContent!.trim(), r.querySelector('.value')!.textContent!.trim()]);
      };

      it('lists the four age bands in words, oldest last, with their counts', async () => {
        await loadBoth();

        expect(rows()).toEqual([
          ['Under 1 day', '4'],
          ['1 to 3 days', '3'],
          ['3 to 7 days', '3'],
          ['Over 7 days', '2'],
        ]);
      });

      it('says so when nothing is open', async () => {
        await setUp();
        const empty = ['Under1Day', '1To3Days', '3To7Days', 'Over7Days'].map((name) => ({ name, count: 0 }));
        summaryRequest().flush({ ...summary, openAgeBuckets: empty });
        statsRequest().flush(statistics);
        await settle();

        expect(text()).toContain('Nothing is open right now.');
      });
    });
  });

  describe('the chosen period', () => {
    it('totals what was created and resolved', async () => {
      await loadBoth();

      expect(tile('Created').textContent).toContain('5');
      expect(tile('Resolved').textContent).toContain('5');
    });

    it('writes the average time and the SLA result in words people read', async () => {
      await loadBoth();

      expect(tile('Average time to resolve').textContent).toContain('4 h 30 min');
      expect(tile('Resolved within SLA').textContent).toContain('83%');
    });

    it('shows a dash, not zero, when there is nothing to average', async () => {
      await loadBoth({ ...statistics, averageResolutionHours: null, slaCompliancePercent: null });

      expect(tile('Average time to resolve').textContent).toContain('—');
      expect(tile('Resolved within SLA').textContent).toContain('—');
    });

    it('draws created and resolved as two named lines', async () => {
      await loadBoth();

      const chart = page().querySelector('app-trend-chart')!;
      expect(chart.querySelector('.legend')!.textContent).toContain('Created');
      expect(chart.querySelector('.legend')!.textContent).toContain('Resolved');
      expect(chart.querySelectorAll('path.line').length).toBe(2);
    });

    it('shows statuses as a donut, in their natural order, with friendly names and counts', async () => {
      await loadBoth();

      const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('By status'))!;
      const names = Array.from(card.querySelectorAll('li.item .label')).map((n) => n.textContent!.trim());
      const counts = Array.from(card.querySelectorAll('li.item .count')).map((n) => n.textContent!.trim());
      expect(names).toEqual(['Open', 'In progress', 'On hold', 'Resolved', 'Closed']);
      expect(counts).toEqual(['6', '4', '2', '8', '10']);
      expect(card.querySelector('.readout')!.textContent).toContain('30');
    });

    it('gives each status its own colour, in a fixed order', async () => {
      await loadBoth();

      const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('By status'))!;
      const colours = Array.from(card.querySelectorAll<HTMLElement>('li.item .key')).map((k) => k.style.background);
      expect(colours.map((c) => c.match(/series-(\d)/)![1])).toEqual(['1', '2', '3', '4', '5']);
    });

    describe('the SLA compliance trend', () => {
      const card = () => Array.from(page().querySelectorAll('mat-card')).find((c) => c.querySelector('mat-card-title')!.textContent!.includes('Resolved within SLA'))!;

      it('draws one line on a 0 to 100% axis, named by its card', async () => {
        await loadBoth();

        const chart = card().querySelector('app-trend-chart')!;
        expect(chart.querySelector('path.line')!.getAttribute('d')).toBe(''); // one point alone has no line to draw: it is a dot
        expect(chart.textContent).toContain('100%');
        expect(chart.querySelector('.legend')).toBeNull();
        expect(card().textContent).toContain('7 days up to it');
      });

      it('leaves a gap, not a zero, for days where nothing was resolved', async () => {
        await loadBoth();

        card().querySelector<HTMLButtonElement>('button.switch')!.click();
        await settle();

        const cells = Array.from(card().querySelectorAll('tbody tr')).map((r) => r.querySelectorAll('td')[0].textContent!.trim());
        expect(cells).toEqual(['no data', '83% · 6 resolved in the last 7 days']);
      });
    });

    describe('average time to resolve, by priority', () => {
      const rows = () => {
        const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('Average time to resolve, by priority'))!;
        return Array.from(card.querySelectorAll('li.row')).map((r) => [r.querySelector('.name')!.textContent!.trim(), r.querySelector('.value')!.textContent!.trim()]);
      };

      it('lists the priorities most urgent first, with the average written as a time', async () => {
        await loadBoth();

        expect(rows()).toEqual([
          ['Critical', '3 h 30 min'],
          ['High', '6 h'],
          ['Medium', '20 h 30 min'],
          ['Low', '—'], // none resolved: a dash, not 0
        ]);
      });

      it('says so when nothing was resolved in the period', async () => {
        await loadBoth({
          ...statistics,
          resolutionByPriority: ['Critical', 'High', 'Medium', 'Low'].map((priority) => ({ priority, averageHours: null, count: 0 })),
        });

        expect(text()).toContain('Nothing was resolved in this period.');
      });
    });

    describe('by priority', () => {
      const card = () => Array.from(page().querySelectorAll('mat-card')).find((c) => c.querySelector('mat-card-title')!.textContent!.includes('By priority'))!;
      const labels = () => Array.from(card().querySelectorAll('li.item .label')).map((n) => n.textContent!.trim());
      const colours = () => Array.from(card().querySelectorAll<HTMLElement>('li.item .key')).map((k) => k.style.background);

      it('is a donut listing priorities from most to least urgent, and calls the missing one "Not set"', async () => {
        await loadBoth();

        expect(labels()).toEqual(['Critical', 'High', 'Medium', 'Low', 'Not set']);
        expect(Array.from(card().querySelectorAll('li.item .count')).map((n) => n.textContent!.trim())).toEqual(['1', '9', '5', '2', '13']);
        expect(card().querySelector('.readout')!.textContent).toContain('30');
      });

      it('uses one blue hue from strongest to lightest for the four priorities, and grey for "Not set"', async () => {
        await loadBoth();

        expect(colours().map((c) => c.match(/ordinal-(\d)|neutral/)![0])).toEqual(['ordinal-1', 'ordinal-2', 'ordinal-3', 'ordinal-4', 'neutral']);
      });
    });

    describe('by category', () => {
      const card = () => Array.from(page().querySelectorAll('mat-card')).find((c) => c.querySelector('mat-card-title')!.textContent!.includes('By category'))!;
      const labels = () => Array.from(card().querySelectorAll('li.item .label')).map((n) => n.textContent!.trim());
      const counts = () => Array.from(card().querySelectorAll('li.item .count')).map((n) => n.textContent!.trim());

      const withCategories = (names: [string, number][]) =>
        loadBoth({ ...statistics, byCategory: names.map(([name, count]) => ({ name, count })) });

      it('is a donut ranking categories from largest to smallest', async () => {
        await loadBoth();

        expect(labels()).toEqual(['Hardware', 'Uncategorized', 'Network']);
        expect(counts()).toEqual(['12', '5', '3']);
      });

      it('gives each category its own colour, in a fixed order', async () => {
        await loadBoth();

        const colours = Array.from(card().querySelectorAll<HTMLElement>('li.item .key')).map((k) => k.style.background);
        expect(colours.map((c) => c.match(/series-(\d)/)![1])).toEqual(['1', '2', '3']);
      });

      it('shows all six when there are exactly six', async () => {
        await withCategories([['A', 6], ['B', 5], ['C', 4], ['D', 3], ['E', 2], ['F', 1]]);

        expect(labels()).toEqual(['A', 'B', 'C', 'D', 'E', 'F']);
      });

      it('with more than six, keeps the biggest five and adds the rest together, saying how many that is', async () => {
        await withCategories([['A', 8], ['B', 7], ['C', 6], ['D', 5], ['E', 4], ['F', 3], ['G', 2], ['H', 1]]);

        expect(labels()).toEqual(['A', 'B', 'C', 'D', 'E', 'All the rest']);
        expect(counts()).toEqual(['8', '7', '6', '5', '4', '6']); // F + G + H = 6
        const rest = card().querySelectorAll('li.item')[5];
        expect(rest.querySelector('.detail')!.textContent).toContain('3 smaller categories');
        expect((rest.querySelector('.key') as HTMLElement).style.background).toContain('--viz-neutral');
      });

      it('keeps the total right when the small ones are added together', async () => {
        await withCategories([['A', 8], ['B', 7], ['C', 6], ['D', 5], ['E', 4], ['F', 3], ['G', 2], ['H', 1]]);

        expect(card().querySelector('.readout')!.textContent).toContain('36');
      });

      it('says so when there is nothing to show', async () => {
        await withCategories([]);

        expect(card().textContent).toContain('Nothing to show for this period.');
      });
    });

    it('asks for new numbers when another period is chosen, keeping the old ones dimmed meanwhile', async () => {
      await loadBoth();

      fixture.debugElement.query(By.directive(MatButtonToggleGroup)).triggerEventHandler('change', { value: 7 });
      await settle();

      const request = statsRequest(7);
      expect(page().querySelector('.stats')!.classList).toContain('stale'); // old numbers still there
      expect(tile('Created').textContent).toContain('5');

      request.flush({ ...statistics, days: 7, createdPerDay: [{ date: '2026-11-17', count: 1 }] });
      await settle();

      expect(page().querySelector('.stats')!.classList).not.toContain('stale');
      expect(tile('Created').textContent).toContain('1');
      expect(text()).toContain('Last 7 days');
    });

    it('offers 7, 30 and 90 days', async () => {
      await loadBoth();

      const labels = Array.from(page().querySelectorAll('mat-button-toggle')).map((t) => t.textContent!.trim());
      expect(labels).toEqual(['7 days', '30 days', '90 days']);
    });
  });

  describe('workload', () => {
    it('shows each assignee to a manager or admin', async () => {
      await loadBoth();

      expect(gridHeadings(page())).toEqual(['Assignee', 'Active', 'Breached']);
      expect(gridRows(page())).toEqual([
        ['dana@example.com', '5', '1'],
        ['lee@example.com', '2', '0'],
      ]);
    });

    it('says so when nobody has active incidents', async () => {
      await loadBoth({ ...statistics, workload: [] });

      expect(text()).toContain('Nobody has active incidents assigned.');
    });

    it('is left out entirely for people who may not see it', async () => {
      await loadBoth({ ...statistics, workload: null });

      expect(text()).not.toContain('Workload by assignee');
    });
  });

  describe('when something fails', () => {
    it('says so, and recovers, when the current numbers cannot be loaded', async () => {
      await setUp();
      summaryRequest().flush({}, { status: 500, statusText: 'Server Error' });
      statsRequest().flush(statistics);
      await settle();

      expect(text()).toContain('The current numbers could not be loaded.');
      expect(text()).toContain('Created'); // the rest of the page still works

      Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Try again'))!.click();
      await settle();
      summaryRequest().flush(summary);
      await settle();

      expect(text()).not.toContain('could not be loaded');
      expect(tile('Active incidents').textContent).toContain('12');
    });

    it('says so, and recovers, when the statistics cannot be loaded', async () => {
      await setUp();
      summaryRequest().flush(summary);
      statsRequest().flush({}, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(text()).toContain('The statistics could not be loaded.');
      expect(tile('Active incidents').textContent).toContain('12'); // the current numbers are unaffected

      Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Try again'))!.click();
      await settle();
      statsRequest().flush(statistics);
      await settle();

      expect(text()).not.toContain('could not be loaded');
      expect(tile('Created').textContent).toContain('5');
    });

    it('keeps the last good statistics on screen if a later request fails', async () => {
      await loadBoth();

      fixture.debugElement.query(By.directive(MatButtonToggleGroup)).triggerEventHandler('change', { value: 90 });
      await settle();
      statsRequest(90).flush({}, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(text()).toContain('The statistics could not be loaded.');
      expect(tile('Created').textContent).toContain('5');
    });
  });
});
