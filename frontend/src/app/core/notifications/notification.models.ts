// The shapes of the notification data. They mirror the C# DTOs in ResolveIQ.Application/DTOs/Notifications.

export interface AppNotification {
  id: string;
  kind: string; // Assigned, Reassigned, Status, Resolved, Reopened, Closed, Comment, Priority, AutoClosed or Info
  message: string;
  incidentId: string | null;
  createdAtUtc: string;
  isRead: boolean;
}

export interface NotificationList {
  /** All the unread ones, not only those in items. */
  unreadCount: number;
  items: AppNotification[];
}
