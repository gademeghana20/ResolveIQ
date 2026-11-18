using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class IncidentSummaryResponse
    {
        public Guid Id { get; set; }

        public string IncidentNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public IncidentPriority? Priority { get; set; }

        public IncidentStatus Status { get; set; }

        public string? CategoryName { get; set; }

        public string ReportedByUserId { get; set; } = string.Empty;

        public string? AssignedToUserId { get; set; }

        /// <summary>The reporter's email, so screens can show a name instead of an id.</summary>
        public string? ReportedByEmail { get; set; }

        /// <summary>The assignee's email. Empty while the incident is unassigned.</summary>
        public string? AssignedToEmail { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public SlaState SlaState { get; set; }

        /// <summary>The SLA deadline. Empty while not started, paused, or without a target.</summary>
        public DateTime? SlaDueAtUtc { get; set; }

        /// <summary>
        /// Whole minutes until the deadline while the clock runs; negative when overdue.
        /// Empty when no clock is running.
        /// </summary>
        public int? SlaMinutesLeft { get; set; }
    }
}
