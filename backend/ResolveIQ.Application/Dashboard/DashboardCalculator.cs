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
                OldestUnassignedWaitingMinutes = oldestWaiting,
                Sla = sla
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
                Workload = includeWorkload ? BuildWorkload(incidents, userEmails, nowUtc) : null
            };
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
