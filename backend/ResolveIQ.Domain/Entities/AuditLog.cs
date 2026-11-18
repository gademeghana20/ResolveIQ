namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// One line in the audit trail: who did what to whom, and when. It copies the names (emails) as they were,
    /// so the line still reads correctly after someone is renamed, and it is never edited or deleted.
    /// </summary>
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Counts up in the order lines are saved (filled in by the database), to order lines made at the same instant.</summary>
        public long Sequence { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Who did it. Empty when the app did it by itself.</summary>
        public string? ActorUserId { get; set; }

        public string? ActorEmail { get; set; }

        /// <summary>What was done, one of the <see cref="ResolveIQ.Domain.Constants.AuditActions"/>.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>The kind of thing it was done to: User, Category or SlaPolicy.</summary>
        public string TargetType { get; set; } = string.Empty;

        public string? TargetId { get; set; }

        /// <summary>A readable name for the target (an email, a category name, a priority).</summary>
        public string? TargetLabel { get; set; }

        /// <summary>What changed, in a sentence ("Role: Employee → Manager").</summary>
        public string? Details { get; set; }
    }
}
