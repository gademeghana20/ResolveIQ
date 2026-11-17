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

    it('lists every SLA state in words, with its count', async () => {
      await loadBoth();

      const card = page().querySelector('app-bar-list')!;
      for (const [name, count] of [
        ['Breached', '3'],
        ['At risk', '2'],
        ['On track', '5'],
        ['Paused', '1'],
        ['Not started', '4'],
        ['Met', '14'],
        ['No SLA', '1'],
      ]) {
        const row = Array.from(card.querySelectorAll('li.row')).find((r) => r.textContent!.includes(name));
        expect(row, name).toBeDefined();
        expect(row!.textContent).toContain(count);
      }
    });

    it('colours breached, at risk and on track by what they mean', async () => {
      await loadBoth();

      const colourOf = (name: string) => {
        const row = Array.from(page().querySelectorAll('li.row')).find((r) => r.querySelector('.name')!.textContent!.trim() === name)!;
        return (row.querySelector('.bar') as HTMLElement).style.background;
      };

      expect(colourOf('Breached')).toContain('--viz-critical');
      expect(colourOf('At risk')).toContain('--viz-warning');
      expect(colourOf('On track')).toContain('--viz-good');
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

    it('lists statuses in their natural order, with friendly names', async () => {
      await loadBoth();

      const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('By status'))!;
      const names = Array.from(card.querySelectorAll('.name')).map((n) => n.textContent!.trim());
      expect(names).toEqual(['Open', 'In progress', 'On hold', 'Resolved', 'Closed']);
    });

    it('lists priorities from most to least urgent, and calls the missing one "Not set"', async () => {
      await loadBoth();

      const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('By priority'))!;
      const names = Array.from(card.querySelectorAll('.name')).map((n) => n.textContent!.trim());
      expect(names).toEqual(['Critical', 'High', 'Medium', 'Low', 'Not set']);
    });

    it('ranks categories from largest to smallest', async () => {
      await loadBoth();

      const card = Array.from(page().querySelectorAll('mat-card')).find((c) => c.textContent!.includes('By category'))!;
      const names = Array.from(card.querySelectorAll('.name')).map((n) => n.textContent!.trim());
      expect(names).toEqual(['Hardware', 'Uncategorized', 'Network']);
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
