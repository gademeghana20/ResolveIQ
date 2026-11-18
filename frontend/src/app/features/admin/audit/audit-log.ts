import { formatDate } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ColDef } from 'ag-grid-community';
import { catchError, debounceTime, distinctUntilChanged, of, switchMap, tap } from 'rxjs';

import { DataGrid } from '../../../shared/data-grid/data-grid';
import { GridFooter, PageChange } from '../../../shared/grid-footer/grid-footer';
import { AdminService } from '../admin.service';
import { AUDIT_ACTIONS, AuditAction, AuditLine, AuditPage, auditActionLabel } from './audit.models';

interface AuditQuery {
  page: number;
  pageSize: number;
  action?: AuditAction;
  search?: string;
}

/** The trail of what Admins (and new sign-ups) did, newest first. Read only. */
@Component({
  selector: 'app-audit-log',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    DataGrid,
    GridFooter,
  ],
  template: `
    <div class="page-title">
      <h1>Audit log</h1>
    </div>

    <p class="explain">
      Who changed what in the Admin area, newest first. Lines are written by the system and cannot be edited or deleted.
    </p>

    <section class="filters" aria-label="Search and filters">
      <mat-form-field appearance="outline" class="search">
        <mat-label>Search</mat-label>
        <input matInput type="search" [formControl]="searchControl" placeholder="Person, category or detail" autocomplete="off" />
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Action</mat-label>
        <mat-select [value]="query().action ?? ''" (selectionChange)="onAction($event.value)">
          <mat-option value="">All</mat-option>
          @for (action of actions; track action.value) {
            <mat-option [value]="action.value">{{ action.label }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </section>

    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading the audit log" />
    }

    @if (loadFailed()) {
      <p class="message error" role="alert">
        The audit log could not be loaded.
        <button mat-button type="button" (click)="retry()">Try again</button>
      </p>
    }

    <app-data-grid
      ariaLabel="Audit log"
      [rows]="result()?.items ?? []"
      [columns]="columnDefs"
      [rowId]="rowId"
      [emptyText]="emptyText()"
    />

    <app-grid-footer
      [total]="result()?.totalCount ?? 0"
      [page]="query().page"
      [pageSize]="query().pageSize"
      (pageChange)="onPage($event)"
    />
  `,
  styles: `
    .page-title {
      margin-bottom: 8px;
    }
    h1 {
      font: var(--mat-sys-headline-medium);
      margin: 0;
    }
    .explain {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
    }
    .filters mat-form-field {
      flex: 0 1 220px;
    }
    .filters .search {
      flex: 1 1 280px;
    }
    .message {
      padding: 24px 16px;
      text-align: center;
      color: var(--mat-sys-on-surface-variant);
    }
    .message.error {
      color: var(--mat-sys-error);
    }
  `,
})
export class AuditLog {
  private readonly admin = inject(AdminService);

  protected readonly actions = AUDIT_ACTIONS;
  protected readonly rowId = (line: AuditLine) => line.id;

  protected readonly query = signal<AuditQuery>({ page: 1, pageSize: 20 });
  protected readonly result = signal<AuditPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly searchControl = new FormControl('', { nonNullable: true });

  protected readonly columnDefs: ColDef<AuditLine>[] = [
    {
      colId: 'when',
      headerName: 'When',
      valueGetter: (p) => (p.data ? formatDate(p.data.createdAtUtc, 'MMM d, y, h:mm a', 'en-US') : ''),
    },
    { colId: 'who', headerName: 'Who', valueGetter: (p) => p.data?.actorEmail ?? 'ResolveIQ (automatic)' },
    { colId: 'action', headerName: 'Action', valueGetter: (p) => (p.data ? auditActionLabel(p.data.action) : '') },
    { colId: 'about', headerName: 'About', valueGetter: (p) => p.data?.targetLabel ?? '—' },
    { colId: 'details', headerName: 'Details', wrapText: true, autoHeight: true, minWidth: 160, valueGetter: (p) => p.data?.details ?? '' },
  ];

  protected readonly emptyText = computed(() => {
    if (this.loading() || this.loadFailed() || this.result()?.items?.length !== 0) {
      return null;
    }
    const q = this.query();
    return q.action || q.search ? 'No lines match your filters.' : 'Nothing has been recorded yet.';
  });

  constructor() {
    toObservable(this.query)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
        }),
        switchMap((query) =>
          this.admin.getAudit(query).pipe(
            catchError(() => {
              this.loadFailed.set(true);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((page) => {
        this.loading.set(false);
        if (page) {
          this.result.set(page);
        }
      });

    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((text) => this.query.update((q) => ({ ...q, search: text, page: 1 })));
  }

  protected onAction(value: AuditAction | ''): void {
    this.query.update((q) => ({ ...q, action: value || undefined, page: 1 }));
  }

  protected onPage(change: PageChange): void {
    this.query.update((q) => ({ ...q, page: change.page, pageSize: change.pageSize }));
  }

  protected retry(): void {
    this.query.update((q) => ({ ...q }));
  }
}
