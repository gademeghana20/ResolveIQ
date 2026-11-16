import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { EMPTY, catchError, forkJoin, of, switchMap, tap } from 'rxjs';

import { Badge } from '../../../shared/badge/badge';
import {
  describeChange,
  priorityLabel,
  priorityTone,
  slaLabel,
  slaTone,
  statusLabel,
  statusTone,
} from '../incident-display';
import { HistoryEntry, IncidentComment, IncidentDetail as IncidentDetailModel } from '../incident.models';
import { IncidentService } from '../incident.service';

type LoadState = 'loading' | 'ready' | 'notFound' | 'failed';

@Component({
  selector: 'app-incident-detail',
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatProgressBarModule, Badge],
  templateUrl: './incident-detail.html',
  styleUrl: './incident-detail.css',
})
export class IncidentDetail {
  private readonly incidents = inject(IncidentService);

  /** The incident id from the address (/incidents/:id), handed in by the router. */
  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly detail = signal<IncidentDetailModel | null>(null);

  // null means "could not be loaded", an empty list means "loaded, nothing there yet".
  protected readonly history = signal<HistoryEntry[] | null>(null);
  protected readonly comments = signal<IncidentComment[] | null>(null);

  // Bumping this reloads the page's data, for the Retry button and, later, after an action.
  private readonly reloadCount = signal(0);
  private readonly request = computed(() => ({ id: this.id(), reload: this.reloadCount() }));

  protected readonly statusLabel = statusLabel;
  protected readonly statusTone = statusTone;
  protected readonly priorityLabel = priorityLabel;
  protected readonly priorityTone = priorityTone;
  protected readonly slaLabel = slaLabel;
  protected readonly slaTone = slaTone;
  protected readonly describeChange = describeChange;

  constructor() {
    toObservable(this.request)
      .pipe(
        tap(() => this.state.set('loading')),
        switchMap(({ id }) =>
          // The incident itself is essential. History and comments are optional extras:
          // if one of them fails, the rest of the page still shows.
          forkJoin({
            detail: this.incidents.getIncident(id),
            history: this.incidents.getHistory(id).pipe(catchError(() => of(null))),
            comments: this.incidents.getComments(id).pipe(catchError(() => of(null))),
          }).pipe(
            catchError((error: unknown) => {
              // The API answers 404 both for "does not exist" and "you may not see it".
              this.state.set(error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'failed');
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ detail, history, comments }) => {
        this.detail.set(detail);
        this.history.set(history);
        this.comments.set(comments);
        this.state.set('ready');
      });
  }

  protected reload(): void {
    this.reloadCount.update((n) => n + 1);
  }
}
