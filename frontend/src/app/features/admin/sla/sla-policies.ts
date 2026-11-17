import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ColDef } from 'ag-grid-community';

import { formatHours } from '../../../shared/format';
import { ActionsCell, BadgeCell, RowAction } from '../../../shared/data-grid/cell-renderers';
import { DataGrid } from '../../../shared/data-grid/data-grid';
import { priorityTone } from '../../incidents/incident-display';
import { IncidentPriority } from '../../incidents/incident.models';
import { AdminService } from '../admin.service';
import { SlaPolicyDialog, SlaPolicyDialogData } from './sla-policy-dialog';
import { SlaPolicy } from './sla.models';

type LoadState = 'loading' | 'ready' | 'failed';

// Most urgent first, whatever order the API sends them in.
const PRIORITY_ORDER: IncidentPriority[] = ['Critical', 'High', 'Medium', 'Low'];

@Component({
  selector: 'app-sla-policies',
  imports: [MatButtonModule, MatProgressBarModule, DataGrid],
  templateUrl: './sla-policies.html',
  styleUrl: './sla-policies.css',
})
export class SlaPolicies {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly rowId = (policy: SlaPolicy) => policy.priority;

  protected readonly columnDefs: ColDef<SlaPolicy>[] = [
    {
      colId: 'priority',
      headerName: 'Priority',
      sortable: true,
      valueGetter: (p) => (p.data ? PRIORITY_ORDER.indexOf(p.data.priority) : 0), // most urgent first, not A to Z
      comparator: (a: number, b: number) => a - b,
      cellRenderer: BadgeCell,
      cellRendererParams: {
        label: (p: SlaPolicy) => p.priority,
        tone: (p: SlaPolicy) => priorityTone(p.priority),
      },
    },
    {
      colId: 'deadline',
      headerName: 'Time to resolve',
      sortable: true,
      comparator: (_a: string, _b: string, x, y) => (x.data?.resolutionTargetHours ?? 0) - (y.data?.resolutionTargetHours ?? 0),
      valueGetter: (p) => (p.data ? this.deadlineText(p.data) : ''),
      cellClassRules: { muted: (p) => !p.data?.isActive },
    },
    {
      colId: 'status',
      headerName: 'Deadline',
      sortable: true,
      valueGetter: (p) => (p.data?.isActive ? 'On' : 'Off'),
      cellRenderer: BadgeCell,
      cellRendererParams: {
        label: (p: SlaPolicy) => (p.isActive ? 'On' : 'Off'),
        tone: (p: SlaPolicy) => (p.isActive ? 'success' : 'neutral'),
      },
    },
    {
      colId: 'actions',
      headerName: 'Actions',
      cellRenderer: ActionsCell,
      cellRendererParams: {
        mode: 'buttons',
        actions: (p: SlaPolicy): RowAction<SlaPolicy>[] => [
          { label: 'Edit', ariaLabel: 'Edit the ' + p.priority + ' deadline', run: (row) => this.edit(row) },
        ],
      },
    },
  ];

  protected readonly state = signal<LoadState>('loading');
  protected readonly policies = signal<SlaPolicy[]>([]);

  protected readonly sorted = computed(() =>
    [...this.policies()].sort((a, b) => PRIORITY_ORDER.indexOf(a.priority) - PRIORITY_ORDER.indexOf(b.priority)),
  );

  protected readonly priorityTone = priorityTone;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set('loading');

    this.admin
      .getSlaPolicies()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (policies) => {
          this.policies.set(policies);
          this.state.set('ready');
        },
        error: () => this.state.set('failed'),
      });
  }

  /** "8 hours (8 h)" reads clearly for small numbers; for big ones the days and hours help: "30 hours (1 d 6 h)". */
  protected deadlineText(policy: SlaPolicy): string {
    const hours = policy.resolutionTargetHours;
    const plain = `${hours} ${hours === 1 ? 'hour' : 'hours'}`;
    return hours >= 24 ? `${plain} (${formatHours(hours)})` : plain;
  }

  protected edit(policy: SlaPolicy): void {
    this.dialog
      .open<SlaPolicyDialog, SlaPolicyDialogData, SlaPolicy>(SlaPolicyDialog, { data: { policy } })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) {
          return;
        }

        this.policies.update((list) => list.map((p) => (p.priority === saved.priority ? saved : p)));
        this.snackBar.open(
          saved.isActive
            ? `${saved.priority} incidents now have ${this.deadlineText(saved)} to be resolved.`
            : `${saved.priority} incidents no longer have a deadline.`,
          'Dismiss',
          { duration: 8000 },
        );
      });
  }
}
