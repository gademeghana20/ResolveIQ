namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// A short message for one person about something that happened on an incident they are involved in
    /// ("INC-000012 was assigned to you"). Shown under the bell in the web app until it is read.
    /// </summary>
    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Counts up in the order notifications are saved (filled in by the database), to order several made at the same instant.</summary>
        public long Sequence { get; set; }

        /// <summary>Who it is for. People only ever see their own.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>The incident it is about, so the message can link to it. Empty for a message not about one incident.</summary>
        public Guid? IncidentId { get; set; }

        /// <summary>What kind of thing happened: Assigned, Reassigned, Status, Resolved, Reopened, Closed, Comment, Priority, AutoClosed, Info.</summary>
        public string Kind { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>When the person read it. Empty while unread.</summary>
        public DateTime? ReadAtUtc { get; set; }
    }
}
