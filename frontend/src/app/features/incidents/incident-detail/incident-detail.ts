import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, computed, inject, input, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { EMPTY, catchError, forkJoin, of, switchMap, tap } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { HasUnsavedChanges, warnBeforeClosingTab } from '../../../core/unsaved-changes';
import { Badge } from '../../../shared/badge/badge';
import { formatDuration } from '../../../shared/format';
import { IncidentActions } from '../actions/incident-actions';
import { AttachmentList } from '../attachments/attachment-list';
import { AttachmentPicker } from '../attachments/attachment-picker';
import { AttachmentService } from '../attachments/attachment.service';
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
import { Attachment, HistoryEntry, IncidentComment, IncidentDetail as IncidentDetailModel } from '../incident.models';
import { IncidentService } from '../incident.service';

type LoadState = 'loading' | 'ready' | 'notFound' | 'failed';

@Component({
  selector: 'app-incident-detail',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatProgressBarModule,
    Badge,
    CommentForm,
    IncidentActions,
    AttachmentList,
    AttachmentPicker,
  ],
  templateUrl: './incident-detail.html',
  styleUrl: './incident-detail.css',
})
export class IncidentDetail implements HasUnsavedChanges {
  private readonly incidents = inject(IncidentService);
  private readonly attachmentService = inject(AttachmentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  /** The incident id from the address (/incidents/:id), handed in by the router. */
  readonly id = input.required<string>();

  private readonly commentForm = viewChild(CommentForm);

  /** True while a comment has been typed, or files chosen, but nothing has been sent. */
  hasUnsavedChanges(): boolean {
    return (this.commentForm()?.hasDraft() ?? false) || this.pendingFiles().length > 0;
  }

  @HostListener('window:beforeunload', ['$event'])
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    warnBeforeClosingTab(event, this);
  }

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

  // Files load on their own, after the incident is already showing. undefined means "still loading",
  // null means "could not be loaded", an empty list means "loaded, nothing attached".
  protected readonly attachments = signal<Attachment[] | null | undefined>(undefined);
  protected readonly noFiles: Attachment[] = [];

  /** The files attached to the incident itself (the ones on comments are shown with their comment). */
  protected readonly incidentFiles = computed(() => (this.attachments() ?? []).filter((a) => !a.commentId));

  protected readonly filesByComment = computed(() => {
    const byComment = new Map<string, Attachment[]>();
    for (const file of this.attachments() ?? []) {
      if (file.commentId) {
        byComment.set(file.commentId, [...(byComment.get(file.commentId) ?? []), file]);
      }
    }
    return byComment;
  });

  private readonly attachmentSettings = toSignal(this.attachmentService.getSettings().pipe(catchError(() => of(null))), {
    initialValue: null,
  });

  /** Whether the page offers "attach files": uploads are on for this site and the incident is not closed. */
  protected readonly canAttach = computed(
    () => this.attachmentSettings()?.enabled === true && this.detail()?.status !== 'Closed',
  );

  protected readonly pendingFiles = signal<File[]>([]);
  protected readonly uploading = signal(false);
  protected readonly uploadMessage = signal<string | null>(null);

  // Bumping this reloads the page's data, for the Retry button and, later, after an action.
  private readonly reloadCount = signal(0);
  private readonly request = computed(() => ({ id: this.id(), reload: this.reloadCount() }));

  protected readonly statusLabel = statusLabel;
  protected readonly statusTone = statusTone;
  protected readonly priorityLabel = priorityLabel;
  protected readonly priorityTone = priorityTone;
  protected readonly slaLabel = slaLabel;
  protected readonly slaTone = slaTone;
  protected readonly formatDuration = formatDuration;
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

    // The files are fetched separately: the page does not wait for them, and a failure here never hides the incident.
    toObservable(this.request)
      .pipe(
        tap(() => this.attachments.set(undefined)),
        switchMap(({ id }) => this.attachmentService.list(id).pipe(catchError(() => of(null)))),
        takeUntilDestroyed(),
      )
      .subscribe((files) => this.attachments.set(files));
  }

  protected reload(): void {
    this.reloadCount.update((n) => n + 1);
  }

  /** An action worked: show the updated incident, and fetch the history, which has a new line. */
  protected onIncidentChanged(updated: IncidentDetailModel): void {
    this.detail.set(updated);
    this.refreshHistory(updated.id);
  }

  /** Files were added or removed: fetch the list again, and the history, which has a new line. */
  protected onFilesChanged(): void {
    const id = this.detail()?.id;
    if (!id) {
      return;
    }

    this.attachmentService
      .list(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (files) => this.attachments.set(files),
        error: () => this.attachments.set(null),
      });
    this.refreshHistory(id);
  }

  /** The "Upload" button: sends the chosen files to the incident, then shows how it went. */
  protected uploadFiles(): void {
    const incident = this.detail();
    const files = this.pendingFiles();
    if (!incident || files.length === 0) {
      return;
    }

    this.uploading.set(true);
    this.uploadMessage.set(null);

    this.attachmentService
      .uploadAll(incident.id, files)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((summary) => {
        this.uploading.set(false);
        this.pendingFiles.set([]);
        this.uploadMessage.set(
          summary.failures.length === 0 ? null : 'Not every file could be attached:\n' + summary.failures.join('\n'),
        );
        this.onFilesChanged();
      });
  }

  private refreshHistory(id: string): void {
    this.incidents
      .getHistory(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entries) => this.history.set(entries),
        error: () => this.history.set(null),
      });
  }

  /** A comment was saved: show it straight away, without reloading the whole page. */
  protected onCommentAdded(comment: IncidentComment): void {
    // Files that came with the comment are saved by now, and the history has a line for each.
    this.onFilesChanged();

    const current = this.comments();
    if (current) {
      this.comments.set([...current, comment]); // the list is oldest first, so the newest goes last
    } else {
      this.reload(); // the list had failed to load; fetch it fresh instead of showing one lonely comment
    }
  }
}
