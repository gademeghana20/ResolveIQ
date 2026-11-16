using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class IncidentDetailResponse
    {
        public Guid Id { get; set; }

        public string IncidentNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public IncidentPriority Priority { get; set; }

        public IncidentStatus Status { get; set; }

        public Guid? CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public string ReportedByUserId { get; set; } = string.Empty;

        public string? AssignedToUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }
    }
}
