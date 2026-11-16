import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable } from 'rxjs';

import { firstValidationMessage } from '../../../core/api-error';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { statusActionLabel } from '../incident-display';
import { IncidentDetail, IncidentPriority, IncidentStatus } from '../incident.models';
import { IncidentService } from '../incident.service';
import { AssignDialog, AssignDialogResult } from './assign-dialog';
import { HoldReasonDialog } from './hold-reason-dialog';
import { PriorityDialog, PriorityDialogData } from './priority-dialog';

/**
 * The action buttons on an incident. It shows only what the API says the current person may do
 * (incident.actions), so a button never appears that the API would then refuse.
 */
@Component({
  selector: 'app-incident-actions',
  imports: [MatButtonModule],
  templateUrl: './incident-actions.html',
  styleUrl: './incident-actions.css',
})
export class IncidentActions {
  private readonly incidents = inject(IncidentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly incident = input.required<IncidentDetail>();

  /** The action worked: here is the incident as it is now. */
  readonly changed = output<IncidentDetail>();

  /** The screen is out of date (someone else changed things, or permissions changed): reload it. */
  readonly stale = output<void>();

  protected readonly busy = signal(false);

  protected statusLabel(target: IncidentStatus): string {
    return statusActionLabel(this.incident().status, target);
  }

  protected hasAnyAction(): boolean {
    const { actions } = this.incident();
    return actions.canAssign || actions.canChangePriority || actions.allowedStatusChanges.length > 0;
  }

  protected moveTo(target: IncidentStatus): void {
    const incident = this.incident();

    if (target === 'OnHold') {
      // Work on hold needs a reason, so ask first.
      this.dialog
        .open<HoldReasonDialog, void, string>(HoldReasonDialog)
        .afterClosed()
        .subscribe((reason) => reason && this.run(this.incidents.changeStatus(incident.id, target, reason)));
      return;
    }

    if (target === 'Closed') {
      // Closed is final, so make sure.
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: 'Close this incident?',
            message: 'A closed incident is final: it cannot be reopened, edited or commented on.',
            confirmLabel: 'Close incident',
          },
        })
        .afterClosed()
        .subscribe((confirmed) => confirmed && this.run(this.incidents.changeStatus(incident.id, target)));
      return;
    }

    this.run(this.incidents.changeStatus(incident.id, target));
  }

  protected assign(): void {
    const incident = this.incident();

    this.dialog
      .open<AssignDialog, unknown, AssignDialogResult>(AssignDialog, {
        data: { currentAssigneeId: incident.assignedToUserId, currentPriority: incident.priority },
      })
      .afterClosed()
      .subscribe((result) => result && this.run(this.incidents.assign(incident.id, result.assigneeUserId, result.priority)));
  }

  protected changePriority(): void {
    const incident = this.incident();

    this.dialog
      .open<PriorityDialog, PriorityDialogData, IncidentPriority>(PriorityDialog, {
        data: { current: incident.priority },
      })
      .afterClosed()
      .subscribe((priority) => priority && this.run(this.incidents.changePriority(incident.id, priority)));
  }

  /** Sends one change to the API and reports what happened. */
  private run(request: Observable<IncidentDetail>): void {
    this.busy.set(true);

    request.subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.changed.emit(updated);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.tell(this.messageFor(error));

        // For these failures what is on screen is out of date, so refresh it.
        if (error instanceof HttpErrorResponse && [403, 404, 409].includes(error.status)) {
          this.stale.emit();
        }
      },
    });
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Cannot reach the server. Nothing was changed.';
      }
      if (error.status === 409) {
        return 'Someone else changed this incident. It has been reloaded, so please try again.';
      }
      if (error.status === 403) {
        return 'You are not allowed to do that. The page has been refreshed.';
      }
      if (error.status === 404) {
        return 'This incident is no longer available.';
      }
      const reason = firstValidationMessage(error);
      if (reason) {
        return reason;
      }
    }
    return 'That could not be done. Please try again.';
  }

  private tell(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 8000 });
  }
}
