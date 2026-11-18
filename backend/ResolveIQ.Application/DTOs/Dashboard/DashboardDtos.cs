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

        /// <summary>Incidents the person reported that are Resolved and waiting for them to confirm the fix (or reopen).</summary>
        public int AwaitingMyConfirmation { get; set; }

        /// <summary>How long the longest-waiting unassigned incident has waited. Empty if there are none.</summary>
        public long? OldestUnassignedWaitingMinutes { get; set; }

        public SlaCounts Sla { get; set; } = new();

        /// <summary>
        /// How long the active incidents have been open, in four fixed bands, oldest last:
        /// Under1Day, 1To3Days, 3To7Days, Over7Days. Always all four, so a chart has no gaps.
        /// </summary>
        public IReadOnlyList<CountItem> OpenAgeBuckets { get; set; } = Array.Empty<CountItem>();
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

    /// <summary>How well deadlines were met around one day.</summary>
    public class SlaComplianceDay
    {
        public DateOnly Date { get; set; }

        /// <summary>
        /// Percent of incidents resolved in the 7 days up to and including this day that met their SLA.
        /// Empty when none of them had an SLA to meet (a trend line has a gap there, not a false 0%).
        /// </summary>
        public double? Percent { get; set; }

        /// <summary>How many incidents (with an SLA) that percentage is based on.</summary>
        public int Resolved { get; set; }
    }

    /// <summary>The average time to resolve for one priority.</summary>
    public class PriorityResolution
    {
        public string Priority { get; set; } = string.Empty;

        /// <summary>Average hours from creation to resolution. Empty when nothing of this priority was resolved.</summary>
        public double? AverageHours { get; set; }

        public int Count { get; set; }
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

        /// <summary>One entry per day in the period: the rolling 7-day SLA compliance.</summary>
        public IReadOnlyList<SlaComplianceDay> SlaComplianceTrend { get; set; } = Array.Empty<SlaComplianceDay>();

        /// <summary>Critical, High, Medium and Low (always all four, in that order): average time to resolve.</summary>
        public IReadOnlyList<PriorityResolution> ResolutionByPriority { get; set; } = Array.Empty<PriorityResolution>();

        /// <summary>Active incidents per assignee. Only filled in for Managers and Admins.</summary>
        public IReadOnlyList<WorkloadItem>? Workload { get; set; }
    }
}
