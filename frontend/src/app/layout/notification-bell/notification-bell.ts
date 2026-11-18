import { Component, ViewEncapsulation, computed, inject } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Router } from '@angular/router';

import { AppNotification } from '../../core/notifications/notification.models';
import { NotificationService } from '../../core/notifications/notification.service';
import { timeAgo } from '../../shared/format';

/**
 * The bell in the top bar: how many notifications are unread, and a list of the latest. Choosing one marks it read and
 * opens the incident it is about. The number is also in the button's name, so it never depends on seeing the badge.
 */
@Component({
  selector: 'app-notification-bell',
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatBadgeModule],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.css',
  encapsulation: ViewEncapsulation.None, // the list opens in an overlay outside this component, so its styles must reach it
})
export class NotificationBell {
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  protected readonly unread = this.notifications.unreadCount;
  protected readonly items = this.notifications.items;

  protected readonly label = computed(() => {
    const count = this.unread();
    return count === 0 ? 'Notifications, none unread' : `Notifications, ${count} unread`;
  });

  protected readonly ago = timeAgo;

  /** Opening the list is a good moment to check for anything newer than the last look. */
  protected onOpen(): void {
    this.notifications.refresh();
  }

  protected open(notification: AppNotification): void {
    this.notifications.markRead(notification).subscribe({ error: () => undefined });

    if (notification.incidentId) {
      this.router.navigate(['/incidents', notification.incidentId]);
    }
  }

  protected markAll(): void {
    this.notifications.markAllRead().subscribe({ error: () => undefined });
  }
}
