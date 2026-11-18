namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// One row in an incident's audit trail: who changed which field, from what, to what, and when.
    /// </summary>
    public class IncidentHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Counts up in the order rows are saved (filled in by the database). Several changes can be saved
        /// at the same instant, so the timestamp alone cannot say which happened first; this can.
        /// </summary>
        public long Sequence { get; set; }

        public Guid IncidentId { get; set; }

        /// <summary>Who made the change. Empty when the app did it by itself (for example closing a long-resolved incident).</summary>
        public string? ChangedByUserId { get; set; }

        public string FieldChanged { get; set; } = string.Empty;

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
