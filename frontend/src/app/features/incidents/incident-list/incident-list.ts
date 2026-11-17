import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, of, switchMap, tap } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { Badge } from '../../../shared/badge/badge';
import { priorityLabel, priorityTone, slaLabel, slaTone, statusLabel, statusTone } from '../incident-display';
import {
  Category,
  INCIDENT_PRIORITIES,
  INCIDENT_STATUSES,
  IncidentPriority,
  IncidentQuery,
  IncidentSortField,
  IncidentStatus,
  IncidentSummary,
  PagedResult,
  SLA_STATES,
  SlaState,
  UserSummary,
} from '../incident.models';
import { IncidentService } from '../incident.service';

type Scope = 'all' | 'assigned' | 'reported' | 'unassigned';

const DEFAULT_QUERY: IncidentQuery = { sortBy: 'CreatedAt', sortDirection: 'Desc', page: 1, pageSize: 20 };

// Table column id (what the header says) -> the sort field the API understands.
const SORT_FIELDS: Record<string, IncidentSortField> = {
  incidentNumber: 'IncidentNumber',
  title: 'Title',
  priority: 'Priority',
  status: 'Status',
  createdAt: 'CreatedAt',
};

@Component({
  selector: 'app-incident-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatTableModule,
    MatSortModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatProgressBarModule,
    RouterLink,
    Badge,
  ],
  templateUrl: './incident-list.html',
  styleUrl: './incident-list.css',
})
export class IncidentList {
  private readonly incidents = inject(IncidentService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  // What the person has asked for. Changing this signal is the only way to load new data.
  // It starts from the address, so a link such as /incidents?slaState=Breached opens already filtered.
  protected readonly query = signal<IncidentQuery>(this.startingQuery());

  protected readonly result = signal<PagedResult<IncidentSummary> | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly categories = signal<Category[]>([]);

  // Only Managers and Admins may look up people, so only they get the Assignee filter.
  protected readonly canFilterByAssignee = this.auth.hasRole('Manager', 'Admin');
  protected readonly people = signal<UserSummary[]>([]);

  protected readonly searchControl = new FormControl('', { nonNullable: true });

  protected readonly columns = ['incidentNumber', 'title', 'priority', 'status', 'category', 'assignee', 'sla', 'createdAt'];
  protected readonly statuses = INCIDENT_STATUSES;
  protected readonly priorities = INCIDENT_PRIORITIES;
  protected readonly slaStates = SLA_STATES;

  // Helpers the template uses to show values.
  protected readonly statusLabel = statusLabel;
  protected readonly statusTone = statusTone;
  protected readonly priorityLabel = priorityLabel;
  protected readonly priorityTone = priorityTone;
  protected readonly slaLabel = slaLabel;
  protected readonly slaTone = slaTone;

  constructor() {
    // Whenever the query changes, ask the API. switchMap drops an older, slower request
    // if a newer one starts, so a late reply can never overwrite a fresher one.
    toObservable(this.query)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
        }),
        switchMap((query) =>
          this.incidents.getIncidents(query).pipe(
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

    // The category filter's choices. If this fails the list still works, just without that filter.
    this.incidents
      .getCategories()
      .pipe(
        catchError(() => of([] as Category[])),
        takeUntilDestroyed(),
      )
      .subscribe((all) => this.categories.set(all.filter((c) => c.isActive)));

    // The people to choose from. Deactivated people stay in the list, because incidents they handled
    // are still worth finding; they are marked in the dropdown. If this fails, the filter simply is not offered.
    if (this.canFilterByAssignee) {
      this.incidents
        .getUsers()
        .pipe(
          catchError(() => of([] as UserSummary[])),
          takeUntilDestroyed(),
        )
        .subscribe((users) =>
          this.people.set([...users].sort((a, b) => (a.email ?? '').localeCompare(b.email ?? '', undefined, { sensitivity: 'base' }))),
        );
    }

    // Search as you type, but wait for a short pause so we do not call the API on every keystroke.
    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((text) => this.applyFilter({ search: text }));
  }

  protected get hasFilters(): boolean {
    const q = this.query();
    return !!(
      q.search ||
      q.status ||
      q.priority ||
      q.categoryId ||
      q.assignedToUserId ||
      q.slaState ||
      q.assignedToMe ||
      q.reportedByMe ||
      q.unassigned
    );
  }

  protected get scope(): Scope {
    const q = this.query();
    return q.assignedToMe ? 'assigned' : q.reportedByMe ? 'reported' : q.unassigned ? 'unassigned' : 'all';
  }

  /**
   * The filters named in the address. Anything that is not a value we know is ignored,
   * so a hand-edited or outdated link can never break the page.
   */
  private startingQuery(): IncidentQuery {
    const params = this.route.snapshot.queryParamMap;
    const scope = params.get('scope') as Scope | null;
    const slaState = params.get('slaState') as SlaState | null;
    const status = params.get('status') as IncidentStatus | null;
    const priority = params.get('priority') as IncidentPriority | null;

    return {
      ...DEFAULT_QUERY,
      slaState: slaState && SLA_STATES.includes(slaState) ? slaState : undefined,
      status: status && INCIDENT_STATUSES.includes(status) ? status : undefined,
      priority: priority && INCIDENT_PRIORITIES.includes(priority) ? priority : undefined,
      assignedToMe: scope === 'assigned' || undefined,
      reportedByMe: scope === 'reported' || undefined,
      unassigned: scope === 'unassigned' || undefined,
    };
  }

  protected open(incident: IncidentSummary): void {
    this.router.navigate(['/incidents', incident.id]);
  }

  protected onStatus(value: IncidentStatus | ''): void {
    this.applyFilter({ status: value || undefined });
  }

  protected onPriority(value: IncidentPriority | ''): void {
    this.applyFilter({ priority: value || undefined });
  }

  protected onCategory(value: string): void {
    this.applyFilter({ categoryId: value || undefined });
  }

  protected onAssignee(userId: string): void {
    // "Assigned to Dana" cannot also be "unassigned" or "assigned to me", so choosing a person replaces those.
    this.applyFilter({
      assignedToUserId: userId || undefined,
      assignedToMe: undefined,
      unassigned: undefined,
    });
  }

  protected onSla(value: SlaState | ''): void {
    this.applyFilter({ slaState: value || undefined });
  }

  protected onScope(scope: Scope): void {
    // A scope and a chosen person would contradict each other, so choosing a scope clears the person.
    this.applyFilter({
      assignedToMe: scope === 'assigned' || undefined,
      reportedByMe: scope === 'reported' || undefined,
      unassigned: scope === 'unassigned' || undefined,
      assignedToUserId: undefined,
    });
  }

  protected onSort(sort: Sort): void {
    const field = SORT_FIELDS[sort.active];
    this.query.update((q) =>
      field && sort.direction
        ? { ...q, sortBy: field, sortDirection: sort.direction === 'asc' ? 'Asc' : 'Desc', page: 1 }
        : { ...q, sortBy: DEFAULT_QUERY.sortBy, sortDirection: DEFAULT_QUERY.sortDirection, page: 1 },
    );
  }

  protected onPage(event: PageEvent): void {
    this.query.update((q) => ({ ...q, page: event.pageIndex + 1, pageSize: event.pageSize }));
  }

  protected clearFilters(): void {
    this.searchControl.setValue('', { emitEvent: false });
    this.query.update((q) => ({ ...DEFAULT_QUERY, sortBy: q.sortBy, sortDirection: q.sortDirection, pageSize: q.pageSize }));
  }

  protected retry(): void {
    this.query.update((q) => ({ ...q })); // a new object, so the list loads again
  }

  /** Any change to the filters starts again from page 1, because page 3 of the old results means nothing now. */
  private applyFilter(change: Partial<IncidentQuery>): void {
    this.query.update((q) => ({ ...q, ...change, page: 1 }));
  }

  /** The sort arrow shown on the header matches the query. */
  protected sortColumn(): string {
    const q = this.query();
    return Object.keys(SORT_FIELDS).find((key) => SORT_FIELDS[key] === q.sortBy) ?? 'createdAt';
  }
}
