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

        /// <summary>The reporter's email, so screens can show a name instead of an id.</summary>
        public string? ReportedByEmail { get; set; }

        /// <summary>The assignee's email. Empty while the incident is unassigned.</summary>
        public string? AssignedToEmail { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        public SlaState SlaState { get; set; }

        public DateTime? SlaStartedAtUtc { get; set; }

        /// <summary>The SLA deadline. Empty while not started, paused, or without a target.</summary>
        public DateTime? SlaDueAtUtc { get; set; }

        /// <summary>What the person asking may do with this incident right now.</summary>
        public IncidentActionsResponse Actions { get; set; } = new();
    }

    /// <summary>
    /// What the current user may do with one incident. Worked out on the server from the same rules
    /// the change endpoints enforce, so a screen can offer exactly the actions that will be accepted.
    /// </summary>
    public class IncidentActionsResponse
    {
        /// <summary>Only the person who reported the incident may reword it, and not once it is closed.</summary>
        public bool CanEdit { get; set; }

        public bool CanAssign { get; set; }

        public bool CanChangePriority { get; set; }

        /// <summary>The statuses this user may move the incident to from its current one.</summary>
        public IList<IncidentStatus> AllowedStatusChanges { get; set; } = new List<IncidentStatus>();

        /// <summary>
        /// Status changes the user may make only because they are a Manager or Admin, which really
        /// belong to the assignee or the reporter. Screens say "on behalf of" for these.
        /// </summary>
        public IList<StatusChangeOnBehalf> OnBehalfOf { get; set; } = new List<StatusChangeOnBehalf>();
    }

    public class StatusChangeOnBehalf
    {
        public IncidentStatus Status { get; set; }

        /// <summary>Whose job this normally is: "Assignee" or "Reporter".</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>That person's email, if known.</summary>
        public string? Email { get; set; }
    }
}
