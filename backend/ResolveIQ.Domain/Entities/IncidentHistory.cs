namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// One row in an incident's audit trail: who changed which field, from what, to what, and when.
    /// </summary>
    public class IncidentHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid IncidentId { get; set; }

        public string ChangedByUserId { get; set; } = string.Empty;

        public string FieldChanged { get; set; } = string.Empty;

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
