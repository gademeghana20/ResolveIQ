namespace ResolveIQ.Application.DTOs.Notifications
{
    public class NotificationResponse
    {
        public Guid Id { get; set; }

        /// <summary>Assigned, Reassigned, Status, Resolved, Reopened, Closed, Comment, Priority, AutoClosed or Info.</summary>
        public string Kind { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        /// <summary>The incident it is about, so the web app can link to it.</summary>
        public Guid? IncidentId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public bool IsRead { get; set; }
    }

    /// <summary>The newest notifications for the person asking, and how many of all of theirs are unread.</summary>
    public class NotificationListResponse
    {
        /// <summary>All unread ones, not just those in <see cref="Items"/>.</summary>
        public int UnreadCount { get; set; }

        public IReadOnlyList<NotificationResponse> Items { get; set; } = Array.Empty<NotificationResponse>();
    }
}
