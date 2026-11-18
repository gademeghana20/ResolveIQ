import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_URL } from '../api-config';
import { AppNotification, NotificationList } from './notification.models';

/** How often the bell looks for news while the page is on screen. */
export const POLL_EVERY_MS = 60_000;

/**
 * The bell's data. It asks the API for the person's notifications now and then (only while the page is visible, so a
 * forgotten tab in the background does not keep calling), and again whenever the person comes back to the tab.
 * Nothing is pushed to the page, so news can be up to a minute old.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);

  private readonly list = signal<NotificationList>({ unreadCount: 0, items: [] });
  private timer: ReturnType<typeof setInterval> | undefined;
  private listening = false;

  readonly unreadCount = computed(() => this.list().unreadCount);
  readonly items = computed(() => this.list().items);

  /** Starts looking for news. Safe to call more than once. */
  start(destroyRef?: DestroyRef): void {
    this.refresh();

    if (this.timer === undefined) {
      this.timer = setInterval(() => this.refreshIfVisible(), POLL_EVERY_MS);
    }

    if (!this.listening && typeof document !== 'undefined') {
      this.listening = true;
      document.addEventListener('visibilitychange', this.onVisibility);
    }

    destroyRef?.onDestroy(() => this.stop());
  }

  /** Stops looking, and forgets what was shown (so the next person to sign in on this browser never sees it). */
  stop(): void {
    clearInterval(this.timer);
    this.timer = undefined;

    if (this.listening && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.onVisibility);
      this.listening = false;
    }

    this.list.set({ unreadCount: 0, items: [] });
  }

  /** Asks the API now. A failed call changes nothing: the bell keeps what it had. */
  refresh(): void {
    this.http.get<NotificationList>(`${API_URL}/notifications`).subscribe({
      next: (list) => this.list.set(list),
      error: () => undefined,
    });
  }

  markRead(notification: AppNotification): Observable<void> {
    // The bell updates at once; the API call follows. If it fails, the next refresh puts things right.
    if (!notification.isRead) {
      this.list.update((current) => ({
        unreadCount: Math.max(0, current.unreadCount - 1),
        items: current.items.map((item) => (item.id === notification.id ? { ...item, isRead: true } : item)),
      }));
    }

    return this.http.put<void>(`${API_URL}/notifications/${notification.id}/read`, {});
  }

  markAllRead(): Observable<void> {
    this.list.update((current) => ({ unreadCount: 0, items: current.items.map((item) => ({ ...item, isRead: true })) }));
    return this.http.put<void>(`${API_URL}/notifications/read-all`, {}).pipe(tap({ error: () => this.refresh() }));
  }

  private readonly onVisibility = (): void => {
    if (document.visibilityState === 'visible') {
      this.refresh();
    }
  };

  private refreshIfVisible(): void {
    if (typeof document === 'undefined' || document.visibilityState === 'visible') {
      this.refresh();
    }
  }
}
