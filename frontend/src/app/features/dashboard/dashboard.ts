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
import { StatTile } from '../../shared/stat-tile/stat-tile';
import { TrendChart, TrendSeries } from '../../shared/trend-chart/trend-chart';
import { slaLabel, statusLabel } from '../incidents/incident-display';
import { IncidentStatus, SlaState } from '../incidents/incident.models';
import { DashboardStatistics, DashboardSummary, PERIOD_CHOICES, WorkloadItem } from './dashboard.models';
import { DashboardService } from './dashboard.service';

type SummaryState = 'loading' | 'ready' | 'failed';

// The order incident statuses and priorities are read in (the API returns them in the same order).
const STATUS_ORDER: IncidentStatus[] = ['Open', 'InProgress', 'OnHold', 'Resolved', 'Closed'];
const PRIORITY_ORDER = ['Critical', 'High', 'Medium', 'Low', 'Unprioritized'];

// SLA states in the order a manager cares about them, and the colour each one means.
// Colour is a hint only: every row also says its state in words.
const SLA_ROWS: { state: SlaState; key: keyof DashboardSummary['sla']; color: string }[] = [
  { state: 'Breached', key: 'breached', color: 'var(--viz-critical)' },
  { state: 'AtRisk', key: 'atRisk', color: 'var(--viz-warning)' },
  { state: 'OnTrack', key: 'onTrack', color: 'var(--viz-good)' },
  { state: 'Paused', key: 'paused', color: 'var(--viz-neutral)' },
  { state: 'NotStarted', key: 'notStarted', color: 'var(--viz-neutral)' },
  { state: 'Met', key: 'met', color: 'var(--viz-good)' },
  { state: 'None', key: 'none', color: 'var(--viz-neutral)' },
];

@Component({
  selector: 'app-dashboard',
  imports: [MatButtonModule, MatButtonToggleModule, MatCardModule, MatProgressBarModule, StatTile, BarList, TrendChart, DataGrid],
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

  protected readonly slaRows = computed<BarItem[]>(() => {
    const sla = this.summary()?.sla;
    return sla ? SLA_ROWS.map((row) => ({ label: slaLabel(row.state), value: sla[row.key], color: row.color })) : [];
  });

  protected readonly statusRows = computed<BarItem[]>(() => {
    const byName = new Map((this.stats()?.byStatus ?? []).map((item) => [item.name, item.count]));
    return STATUS_ORDER.map((status) => ({ label: statusLabel(status), value: byName.get(status) ?? 0 }));
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
