import { inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { CanDeactivateFn } from '@angular/router';
import { map } from 'rxjs';

import { ConfirmDialog } from '../shared/confirm-dialog/confirm-dialog';
import { AuthService } from './auth/auth.service';

/** A page that knows whether the person has typed something they have not saved yet. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Put on a route: before leaving a page with unsaved typing, asks "Leave this page?".
 * Closing or refreshing the browser tab is covered separately by each page (see warnBeforeClosingTab).
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (page) => {
  // Once the session is over (signed out), there is nothing to protect any more and nowhere to save to.
  if (!page.hasUnsavedChanges() || !inject(AuthService).isLoggedIn()) {
    return true;
  }

  return inject(MatDialog)
    .open(ConfirmDialog, {
      data: {
        title: 'Leave this page?',
        message: 'You have typed something that is not saved. If you leave now, it will be lost.',
        confirmLabel: 'Leave page',
      },
    })
    .afterClosed()
    .pipe(map((leave) => leave === true));
};

/** For a page's `window:beforeunload` listener: makes the browser ask before the tab is closed or refreshed. */
export function warnBeforeClosingTab(event: BeforeUnloadEvent, page: HasUnsavedChanges): void {
  if (page.hasUnsavedChanges()) {
    event.preventDefault();
  }
}
