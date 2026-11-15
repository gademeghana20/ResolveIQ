
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Domain.Entities
{
    public class Incident
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string IncidentNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public IncidentPriority Priority { get; set; }

        public IncidentStatus Status { get; set; } = IncidentStatus.Open;

        public Guid? CategoryId { get; set; }

        public Category? Category { get; set; }

        public string ReportedByUserId { get; set; } = string.Empty;

        public string? AssignedToUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }
    }
}
