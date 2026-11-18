using ResolveIQ.Application.DTOs.Dashboard;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;

namespace ResolveIQ.Application.Dashboard
{
    /// <summary>
    /// Turns a list of incidents into dashboard numbers. Pure logic: it never touches the database
    /// and is handed the current time, so tests can use fixed incidents and a fixed "now".
    /// It counts in memory, which is fine for thousands of incidents; a very large system would
    /// move these counts into SQL or a saved summary.
    /// </summary>
    public static class DashboardCalculator
    {
        public const string UnprioritizedLabel = "Unprioritized";
        public const string UncategorizedLabel = "Uncategorized";

        // The four age bands of an open incident, youngest first. The names are stable keys the web app labels.
        public static readonly string[] AgeBandNames = { "Under1Day", "1To3Days", "3To7Days", "Over7Days" };

        // How many days back the SLA compliance trend looks from each day. A single day often has no
        // resolutions at all, so each point looks at the week up to it.
        public const int ComplianceWindowDays = 7;

        public static DashboardSummaryResponse BuildSummary(
            IReadOnlyList<Incident> incidents,
            string currentUserId,
            DateTime nowUtc)
        {
            var sla = new SlaCounts();
            foreach (var incident in incidents)
            {
                switch (StateOf(incident, nowUtc))
                {
                    case SlaState.NotStarted: sla.NotStarted++; break;
                    case SlaState.OnTrack: sla.OnTrack++; break;
                    case SlaState.AtRisk: sla.AtRisk++; break;
                    case SlaState.Paused: sla.Paused++; break;
                    case SlaState.Breached: sla.Breached++; break;
                    case SlaState.Met: sla.Met++; break;
                    default: sla.None++; break;
                }
            }

            var unassigned = incidents.Where(i => i.AssignedToUserId is null).ToList();
            long? oldestWaiting = unassigned.Count == 0
                ? null
                : (long)Math.Max(0, (nowUtc - unassigned.Min(i => i.CreatedAtUtc)).TotalMinutes);

            return new DashboardSummaryResponse
            {
                TotalIncidents = incidents.Count,
                ActiveIncidents = incidents.Count(IsActive),
                AssignedToMe = incidents.Count(i => IsActive(i) && i.AssignedToUserId == currentUserId),
                Unassigned = unassigned.Count,
                AwaitingMyConfirmation = incidents.Count(i => i.Status == IncidentStatus.Resolved && i.ReportedByUserId == currentUserId),
                OldestUnassignedWaitingMinutes = oldestWaiting,
                Sla = sla,
                OpenAgeBuckets = BuildAgeBuckets(incidents, nowUtc)
            };
        }

        public static DashboardStatisticsResponse BuildStatistics(
            IReadOnlyList<Incident> incidents,
            IReadOnlyDictionary<Guid, string> categoryNames,
            IReadOnlyDictionary<string, string> userEmails,
            bool includeWorkload,
            int days,
            DateTime nowUtc)
        {
            var today = DateOnly.FromDateTime(nowUtc);
            var from = today.AddDays(-(days - 1));
            var windowStart = from.ToDateTime(TimeOnly.MinValue);

            // Incidents resolved inside the period (a reopened incident has no resolved time, so it is left out).
            var resolvedInWindow = incidents
                .Where(i => i.ResolvedAtUtc is DateTime resolvedAt && resolvedAt >= windowStart)
                .ToList();

            double? averageHours = resolvedInWindow.Count == 0
                ? null
                : Math.Round(resolvedInWindow.Average(i => (i.ResolvedAtUtc!.Value - i.CreatedAtUtc).TotalHours), 1);

            var met = resolvedInWindow.Count(i => StateOf(i, nowUtc) == SlaState.Met);
            var breached = resolvedInWindow.Count(i => StateOf(i, nowUtc) == SlaState.Breached);
            double? compliance = met + breached == 0
                ? null
                : Math.Round(100.0 * met / (met + breached), 1);

            return new DashboardStatisticsResponse
            {
                Days = days,
                From = from,
                To = today,
                ByStatus = Enum.GetValues<IncidentStatus>()
                    .OrderBy(s => (int)s)
                    .Select(s => new CountItem { Name = s.ToString(), Count = incidents.Count(i => i.Status == s) })
                    .ToList(),
                ByPriority = BuildPriorityBreakdown(incidents),
                ByCategory = BuildCategoryBreakdown(incidents, categoryNames),
                CreatedPerDay = PerDay(from, today, incidents.Select(i => DateOnly.FromDateTime(i.CreatedAtUtc))),
                ResolvedPerDay = PerDay(from, today, incidents
                    .Where(i => i.ResolvedAtUtc.HasValue)
                    .Select(i => DateOnly.FromDateTime(i.ResolvedAtUtc!.Value))),
                AverageResolutionHours = averageHours,
                SlaCompliancePercent = compliance,
                SlaComplianceTrend = BuildComplianceTrend(incidents, from, today, nowUtc),
                ResolutionByPriority = BuildResolutionByPriority(resolvedInWindow),
                Workload = includeWorkload ? BuildWorkload(incidents, userEmails, nowUtc) : null
            };
        }

        // Active incidents grouped by how long they have been open. All four bands are always listed.
        private static List<CountItem> BuildAgeBuckets(IReadOnlyList<Incident> incidents, DateTime nowUtc)
        {
            var counts = incidents
                .Where(IsActive)
                .GroupBy(i => AgeBand(nowUtc - i.CreatedAtUtc))
                .ToDictionary(g => g.Key, g => g.Count());

            return AgeBandNames
                .Select(name => new CountItem { Name = name, Count = counts.GetValueOrDefault(name) })
                .ToList();
        }

        private static string AgeBand(TimeSpan age) =>
            age.TotalDays < 1 ? AgeBandNames[0]
            : age.TotalDays < 3 ? AgeBandNames[1]
            : age.TotalDays < 7 ? AgeBandNames[2]
            : AgeBandNames[3];

        // For each day: of the incidents (with an SLA) resolved in the week up to that day, the share that met it.
        private static List<SlaComplianceDay> BuildComplianceTrend(
            IReadOnlyList<Incident> incidents,
            DateOnly from,
            DateOnly to,
            DateTime nowUtc)
        {
            // Only incidents resolved with an SLA to measure count. Each is Met or Breached.
            var judged = incidents
                .Where(i => i.ResolvedAtUtc.HasValue)
                .Select(i => (Day: DateOnly.FromDateTime(i.ResolvedAtUtc!.Value), State: StateOf(i, nowUtc)))
                .Where(x => x.State is SlaState.Met or SlaState.Breached)
                .ToList();

            var result = new List<SlaComplianceDay>();

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var windowStart = day.AddDays(-(ComplianceWindowDays - 1));
                var inWindow = judged.Where(x => x.Day >= windowStart && x.Day <= day).ToList();
                var met = inWindow.Count(x => x.State == SlaState.Met);

                result.Add(new SlaComplianceDay
                {
                    Date = day,
                    Resolved = inWindow.Count,
                    Percent = inWindow.Count == 0 ? null : Math.Round(100.0 * met / inWindow.Count, 1)
                });
            }

            return result;
        }

        // Average time to resolve for each priority, for the incidents resolved in the period.
        private static List<PriorityResolution> BuildResolutionByPriority(IReadOnlyList<Incident> resolvedInWindow)
        {
            return Enum.GetValues<IncidentPriority>()
                .OrderByDescending(p => (int)p)
                .Select(p =>
                {
                    var ofPriority = resolvedInWindow.Where(i => i.Priority == p).ToList();

                    return new PriorityResolution
                    {
                        Priority = p.ToString(),
                        Count = ofPriority.Count,
                        AverageHours = ofPriority.Count == 0
                            ? null
                            : Math.Round(ofPriority.Average(i => (i.ResolvedAtUtc!.Value - i.CreatedAtUtc).TotalHours), 1)
                    };
                })
                .ToList();
        }

        private static bool IsActive(Incident i) =>
            i.Status is IncidentStatus.Open or IncidentStatus.InProgress or IncidentStatus.OnHold;

        // The state comes from what each incident was promised, so changing a policy later cannot rewrite it.
        private static SlaState StateOf(Incident incident, DateTime nowUtc) =>
            SlaCalculator.Evaluate(incident, nowUtc).State;

        private static List<CountItem> BuildPriorityBreakdown(IReadOnlyList<Incident> incidents)
        {
            var items = Enum.GetValues<IncidentPriority>()
                .OrderByDescending(p => (int)p)
                .Select(p => new CountItem { Name = p.ToString(), Count = incidents.Count(i => i.Priority == p) })
                .ToList();

            items.Add(new CountItem { Name = UnprioritizedLabel, Count = incidents.Count(i => i.Priority is null) });
            return items;
        }

        private static List<CountItem> BuildCategoryBreakdown(
            IReadOnlyList<Incident> incidents,
            IReadOnlyDictionary<Guid, string> categoryNames)
        {
            return incidents
                .GroupBy(i => i.CategoryId is Guid id && categoryNames.TryGetValue(id, out var name)
                    ? name
                    : UncategorizedLabel)
                .Select(g => new CountItem { Name = g.Key, Count = g.Count() })
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Name)
                .ToList();
        }

        // One entry for every day in the period, including days with nothing, so a chart has no gaps.
        private static List<DailyCount> PerDay(DateOnly from, DateOnly to, IEnumerable<DateOnly> dates)
        {
            var counts = dates.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
            var result = new List<DailyCount>();

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                result.Add(new DailyCount { Date = day, Count = counts.GetValueOrDefault(day) });
            }

            return result;
        }

        private static List<WorkloadItem> BuildWorkload(
            IReadOnlyList<Incident> incidents,
            IReadOnlyDictionary<string, string> userEmails,
            DateTime nowUtc)
        {
            return incidents
                .Where(i => IsActive(i) && i.AssignedToUserId is not null)
                .GroupBy(i => i.AssignedToUserId!)
                .Select(g => new WorkloadItem
                {
                    UserId = g.Key,
                    Email = userEmails.GetValueOrDefault(g.Key),
                    ActiveIncidents = g.Count(),
                    BreachedIncidents = g.Count(i => StateOf(i, nowUtc) == SlaState.Breached)
                })
                .OrderByDescending(w => w.ActiveIncidents)
                .ThenBy(w => w.Email)
                .ToList();
        }
    }
}
