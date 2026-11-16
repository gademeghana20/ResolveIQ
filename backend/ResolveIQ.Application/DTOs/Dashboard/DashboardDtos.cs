namespace ResolveIQ.Application.DTOs.Dashboard
{
    /// <summary>How many incidents are in each SLA state.</summary>
    public class SlaCounts
    {
        public int NotStarted { get; set; }

        public int OnTrack { get; set; }

        public int AtRisk { get; set; }

        public int Paused { get; set; }

        public int Breached { get; set; }

        public int Met { get; set; }

        /// <summary>No SLA target applies (for example, no active policy).</summary>
        public int None { get; set; }
    }

    /// <summary>The "what needs attention now" numbers.</summary>
    public class DashboardSummaryResponse
    {
        public int TotalIncidents { get; set; }

        /// <summary>Open, InProgress and OnHold (not yet resolved or closed).</summary>
        public int ActiveIncidents { get; set; }

        /// <summary>Active incidents assigned to the person asking.</summary>
        public int AssignedToMe { get; set; }

        public int Unassigned { get; set; }

        /// <summary>How long the longest-waiting unassigned incident has waited. Empty if there are none.</summary>
        public long? OldestUnassignedWaitingMinutes { get; set; }

        public SlaCounts Sla { get; set; } = new();
    }

    public class CountItem
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    public class DailyCount
    {
        public DateOnly Date { get; set; }

        public int Count { get; set; }
    }

    public class WorkloadItem
    {
        public string UserId { get; set; } = string.Empty;

        public string? Email { get; set; }

        public int ActiveIncidents { get; set; }

        public int BreachedIncidents { get; set; }
    }

    public class DashboardStatisticsQuery
    {
        public const int MaxDays = 365;

        [System.ComponentModel.DataAnnotations.Range(1, MaxDays)]
        public int Days { get; set; } = 30;
    }

    /// <summary>The "how are we doing" numbers.</summary>
    public class DashboardStatisticsResponse
    {
        public int Days { get; set; }

        public DateOnly From { get; set; }

        public DateOnly To { get; set; }

        public IReadOnlyList<CountItem> ByStatus { get; set; } = Array.Empty<CountItem>();

        public IReadOnlyList<CountItem> ByPriority { get; set; } = Array.Empty<CountItem>();

        public IReadOnlyList<CountItem> ByCategory { get; set; } = Array.Empty<CountItem>();

        public IReadOnlyList<DailyCount> CreatedPerDay { get; set; } = Array.Empty<DailyCount>();

        public IReadOnlyList<DailyCount> ResolvedPerDay { get; set; } = Array.Empty<DailyCount>();

        /// <summary>Average hours from creation to resolution, for incidents resolved in the period. Empty if none.</summary>
        public double? AverageResolutionHours { get; set; }

        /// <summary>Percent of incidents resolved in the period that met their SLA. Empty if none had an SLA.</summary>
        public double? SlaCompliancePercent { get; set; }

        /// <summary>Active incidents per assignee. Only filled in for Managers and Admins.</summary>
        public IReadOnlyList<WorkloadItem>? Workload { get; set; }
    }
}
