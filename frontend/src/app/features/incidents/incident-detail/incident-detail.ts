import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { EMPTY, catchError, forkJoin, of, switchMap, tap } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { Badge } from '../../../shared/badge/badge';
import { IncidentActions } from '../actions/incident-actions';
import { CommentForm } from '../comment-form/comment-form';
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
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatProgressBarModule, Badge, CommentForm, IncidentActions],
  templateUrl: './incident-detail.html',
  styleUrl: './incident-detail.css',
})
export class IncidentDetail {
  private readonly incidents = inject(IncidentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  /** The incident id from the address (/incidents/:id), handed in by the router. */
  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly detail = signal<IncidentDetailModel | null>(null);

  // null means "could not be loaded", an empty list means "loaded, nothing there yet".
  protected readonly history = signal<HistoryEntry[] | null>(null);

  /** Whether the person looking reported this incident (they are the one who confirms a fix). */
  protected readonly isReporter = computed(() => {
    const incident = this.detail();
    return !!incident && incident.reportedByUserId === this.auth.userId();
  });
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

  /** An action worked: show the updated incident, and fetch the history, which has a new line. */
  protected onIncidentChanged(updated: IncidentDetailModel): void {
    this.detail.set(updated);

    this.incidents
      .getHistory(updated.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entries) => this.history.set(entries),
        error: () => this.history.set(null),
      });
  }

  /** A comment was saved: show it straight away, without reloading the whole page. */
  protected onCommentAdded(comment: IncidentComment): void {
    const current = this.comments();
    if (current) {
      this.comments.set([...current, comment]); // the list is oldest first, so the newest goes last
    } else {
      this.reload(); // the list had failed to load; fetch it fresh instead of showing one lonely comment
    }
  }
}
