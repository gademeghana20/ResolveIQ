import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ColDef } from 'ag-grid-community';
import { EMPTY, catchError, switchMap, tap } from 'rxjs';

import { formatDuration, formatHours, formatPercent } from '../../shared/format';
import { DataGrid } from '../../shared/data-grid/data-grid';
import { BarItem, BarList } from '../../shared/bar-list/bar-list';
import { DonutChart, DonutItem } from '../../shared/donut-chart/donut-chart';
import { StatTile } from '../../shared/stat-tile/stat-tile';
import { TrendChart, TrendSeries } from '../../shared/trend-chart/trend-chart';
import { slaLabel, statusLabel } from '../incidents/incident-display';
import { IncidentStatus } from '../incidents/incident.models';
import { DashboardStatistics, DashboardSummary, PERIOD_CHOICES, WorkloadItem } from './dashboard.models';
import { DashboardService } from './dashboard.service';

type SummaryState = 'loading' | 'ready' | 'failed';

// The order incident statuses and priorities are read in (the API returns them in the same order).
const STATUS_ORDER: IncidentStatus[] = ['Open', 'InProgress', 'OnHold', 'Resolved', 'Closed'];
const PRIORITY_ORDER = ['Critical', 'High', 'Medium', 'Low', 'Unprioritized'];

// The first five categorical colours, in their fixed order (checked with the palette validator).
// A status keeps its colour whatever else is shown.
const STATUS_COLORS = [
  'var(--viz-series-1)',
  'var(--viz-series-2)',
  'var(--viz-series-3)',
  'var(--viz-series-4)',
  'var(--viz-series-5)',
];

// What each age band is called on screen. The API sends stable keys.
const AGE_LABELS: Record<string, string> = {
  Under1Day: 'Under 1 day',
  '1To3Days': '1 to 3 days',
  '3To7Days': '3 to 7 days',
  Over7Days: 'Over 7 days',
};

@Component({
  selector: 'app-dashboard',
  imports: [MatButtonModule, MatButtonToggleModule, MatCardModule, MatProgressBarModule, StatTile, BarList, DonutChart, TrendChart, DataGrid],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard {
  private readonly dashboard = inject(DashboardService);

  protected readonly periods = PERIOD_CHOICES;

  protected readonly workloadRowId = (person: WorkloadItem) => person.userId;
  protected readonly workloadColumns: ColDef<WorkloadItem>[] = [
    { colId: 'assignee', headerName: 'Assignee', sortable: true, valueGetter: (p) => p.data?.email ?? 'Unknown user' },
    { colId: 'active', headerName: 'Active', sortable: true, field: 'activeIncidents', cellClass: ['ag-right-aligned-cell', 'numeric'], headerClass: 'ag-right-aligned-header' },
    { colId: 'breached', headerName: 'Breached', sortable: true, field: 'breachedIncidents', cellClass: ['ag-right-aligned-cell', 'numeric'], headerClass: 'ag-right-aligned-header' },
  ];
  protected readonly days = signal<number>(30);

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly summaryState = signal<SummaryState>('loading');

  // The last statistics stay on screen while new ones load (dimmed), so the page does not jump.
  protected readonly stats = signal<DashboardStatistics | null>(null);
  protected readonly statsLoading = signal(true);
  protected readonly statsFailed = signal(false);

  private readonly summaryReload = signal(0);
  private readonly statsReload = signal(0);

  /**
   * The SLA states as five parts of a donut. Three states mean "no clock is running" (paused, not started, no SLA),
   * so they share one part, and the legend spells out what is inside it.
   */
  protected readonly slaDonut = computed<DonutItem[]>(() => {
    const sla = this.summary()?.sla;
    if (!sla) {
      return [];
    }

    const notRunning = sla.paused + sla.notStarted + sla.none;

    return [
      { label: slaLabel('Breached'), value: sla.breached, color: 'var(--viz-critical)' },
      { label: slaLabel('AtRisk'), value: sla.atRisk, color: 'var(--viz-warning)' },
      { label: slaLabel('OnTrack'), value: sla.onTrack, color: 'var(--viz-good)' },
      { label: slaLabel('Met'), value: sla.met, color: 'var(--viz-series-1)' },
      {
        label: 'No clock running',
        value: notRunning,
        color: 'var(--viz-neutral)',
        detail: `${slaLabel('Paused')} ${sla.paused} · ${slaLabel('NotStarted')} ${sla.notStarted} · ${slaLabel('None')} ${sla.none}`,
      },
    ];
  });

  /** How long the open incidents have been waiting, oldest last. */
  protected readonly ageRows = computed<BarItem[]>(() =>
    (this.summary()?.openAgeBuckets ?? []).map((bucket) => ({ label: AGE_LABELS[bucket.name] ?? bucket.name, value: bucket.count })),
  );

  protected readonly statusDonut = computed<DonutItem[]>(() => {
    const byName = new Map((this.stats()?.byStatus ?? []).map((item) => [item.name, item.count]));
    return STATUS_ORDER.map((status, index) => ({
      label: statusLabel(status),
      value: byName.get(status) ?? 0,
      color: STATUS_COLORS[index],
    }));
  });

  protected readonly priorityRows = computed<BarItem[]>(() => {
    const byName = new Map((this.stats()?.byPriority ?? []).map((item) => [item.name, item.count]));
    return PRIORITY_ORDER.map((name) => ({ label: name === 'Unprioritized' ? 'Not set' : name, value: byName.get(name) ?? 0 }));
  });

  // Categories have no natural order, so they are ranked by size.
  protected readonly categoryRows = computed<BarItem[]>(() =>
    [...(this.stats()?.byCategory ?? [])]
      .sort((a, b) => b.count - a.count)
      .map((item) => ({ label: item.name, value: item.count })),
  );

  protected readonly trend = computed<TrendSeries[]>(() => {
    const stats = this.stats();
    return stats
      ? [
          { name: 'Created', color: 'var(--viz-series-1)', points: stats.createdPerDay.map((d) => ({ date: d.date, value: d.count })) },
          { name: 'Resolved', color: 'var(--viz-series-2)', points: stats.resolvedPerDay.map((d) => ({ date: d.date, value: d.count })) },
        ]
      : [];
  });

  /** The average time to resolve, for each priority (most urgent first). A priority with nothing resolved shows a dash. */
  protected readonly resolutionRows = computed<BarItem[]>(() =>
    (this.stats()?.resolutionByPriority ?? []).map((row) => ({
      label: row.priority,
      value: row.averageHours ?? 0,
      valueText: row.averageHours === null ? '—' : formatHours(row.averageHours),
    })),
  );

  /** One line: the share of resolved incidents that met their SLA, over the week up to each day. Gaps where none were resolved. */
  protected readonly complianceTrend = computed<TrendSeries[]>(() => {
    const trend = this.stats()?.slaComplianceTrend;
    return trend
      ? [
          {
            name: 'Resolved within SLA',
            color: 'var(--viz-series-1)',
            points: trend.map((day) => ({
              date: day.date,
              value: day.percent,
              note: day.resolved > 0 ? `${day.resolved} resolved in the last 7 days` : undefined,
            })),
          },
        ]
      : [];
  });

  protected readonly formatPercent = formatPercent;

  protected readonly createdTotal = computed(() => (this.stats()?.createdPerDay ?? []).reduce((sum, d) => sum + d.count, 0));
  protected readonly resolvedTotal = computed(() => (this.stats()?.resolvedPerDay ?? []).reduce((sum, d) => sum + d.count, 0));

  protected readonly oldestWaiting = computed(() => {
    const minutes = this.summary()?.oldestUnassignedWaitingMinutes;
    return minutes === null || minutes === undefined ? null : `oldest has waited ${formatDuration(minutes)}`;
  });

  protected readonly averageResolution = computed(() => {
    const hours = this.stats()?.averageResolutionHours;
    return hours === null || hours === undefined ? '—' : formatHours(hours);
  });

  protected readonly slaCompliance = computed(() => {
    const percent = this.stats()?.slaCompliancePercent;
    return percent === null || percent === undefined ? '—' : formatPercent(percent);
  });

  constructor() {
    toObservable(this.summaryReload)
      .pipe(
        tap(() => this.summaryState.set('loading')),
        switchMap(() =>
          this.dashboard.getSummary().pipe(
            catchError(() => {
              this.summaryState.set('failed');
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((summary) => {
        this.summary.set(summary);
        this.summaryState.set('ready');
      });

    toObservable(computed(() => ({ days: this.days(), reload: this.statsReload() })))
      .pipe(
        tap(() => {
          this.statsLoading.set(true);
          this.statsFailed.set(false);
        }),
        switchMap(({ days }) =>
          this.dashboard.getStatistics(days).pipe(
            catchError(() => {
              this.statsFailed.set(true);
              this.statsLoading.set(false);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((stats) => {
        this.stats.set(stats);
        this.statsLoading.set(false);
      });
  }

  protected setDays(days: number): void {
    this.days.set(days);
  }

  protected retrySummary(): void {
    this.summaryReload.update((n) => n + 1);
  }

  protected retryStats(): void {
    this.statsReload.update((n) => n + 1);
  }
}
