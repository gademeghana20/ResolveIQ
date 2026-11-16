using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class IncidentSummaryResponse
    {
        public Guid Id { get; set; }

        public string IncidentNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public IncidentPriority Priority { get; set; }

        public IncidentStatus Status { get; set; }

        public string? CategoryName { get; set; }

        public string ReportedByUserId { get; set; } = string.Empty;

        public string? AssignedToUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
