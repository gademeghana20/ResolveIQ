using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class IncidentDetailResponse
    {
        public Guid Id { get; set; }

        public string IncidentNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public IncidentPriority? Priority { get; set; }

        public IncidentStatus Status { get; set; }

        public Guid? CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public string ReportedByUserId { get; set; } = string.Empty;

        public string? AssignedToUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        public SlaState SlaState { get; set; }

        public DateTime? SlaStartedAtUtc { get; set; }

        /// <summary>The SLA deadline. Empty while not started, paused, or without a target.</summary>
        public DateTime? SlaDueAtUtc { get; set; }
    }
}
