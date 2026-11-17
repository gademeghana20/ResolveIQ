using ResolveIQ.Application.Dashboard;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Tests.Dashboard
{
    /// <summary>
    /// The numbers behind the dashboard's analytics charts: how old open incidents are, how well deadlines are met
    /// over time, and how long each priority takes to resolve. Everything is worked out by hand from fixed incidents.
    /// </summary>
    public class DashboardAnalyticsTests
    {
        // Fixed "now": Friday 2026-11-20 12:00 UTC.
        private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static readonly IReadOnlyDictionary<Guid, string> NoCategories = new Dictionary<Guid, string>();
        private static readonly IReadOnlyDictionary<string, string> NoPeople = new Dictionary<string, string>();

        private static DateTime Ago(double hours) => Now.AddHours(-hours);

        private static Incident Active(double ageHours, IncidentStatus status = IncidentStatus.Open) =>
            new() { Status = status, CreatedAtUtc = Ago(ageHours) };

        // A resolved incident that had been promised `targetHours` and took `tookHours` (resolved `resolvedHoursAgo` ago).
        private static Incident Resolved(IncidentPriority priority, int targetHours, double tookHours, double resolvedHoursAgo)
        {
            var resolvedAt = Ago(resolvedHoursAgo);
            var created = resolvedAt.AddHours(-tookHours);

            return new Incident
            {
                Status = IncidentStatus.Resolved,
                Priority = priority,
                AssignedToUserId = "P",
                CreatedAtUtc = created,
                SlaStartedAtUtc = created,
                SlaTargetHours = targetHours,
                ResolvedAtUtc = resolvedAt
            };
        }

        private static Application.DTOs.Dashboard.DashboardStatisticsResponse Stats(IReadOnlyList<Incident> incidents, int days = 7) =>
            DashboardCalculator.BuildStatistics(incidents, NoCategories, NoPeople, false, days, Now);

        // ---- how old the open incidents are ----

        [Fact]
        public void Age_GroupsActiveIncidentsIntoFourBands_OldestLast()
        {
            var incidents = new List<Incident>
            {
                Active(3), Active(23.9),                         // under a day
                Active(24), Active(30), Active(71.9),            // 1 to 3 days (24 hours is already a day)
                Active(72), Active(100), Active(167.9),          // 3 to 7 days
                Active(168), Active(500)                         // over 7 days
            };

            var buckets = DashboardCalculator.BuildSummary(incidents, "x", Now).OpenAgeBuckets;

            Assert.Equal(new[] { "Under1Day", "1To3Days", "3To7Days", "Over7Days" }, buckets.Select(b => b.Name));
            Assert.Equal(new[] { 2, 3, 3, 2 }, buckets.Select(b => b.Count));
        }

        [Fact]
        public void Age_CountsOnHoldAndInProgress_ButNotResolvedOrClosed()
        {
            var incidents = new List<Incident>
            {
                Active(10, IncidentStatus.InProgress),
                Active(10, IncidentStatus.OnHold),
                Active(10, IncidentStatus.Resolved),
                Active(500, IncidentStatus.Closed)
            };

            var buckets = DashboardCalculator.BuildSummary(incidents, "x", Now).OpenAgeBuckets;

            Assert.Equal(new[] { 2, 0, 0, 0 }, buckets.Select(b => b.Count));
        }

        [Fact]
        public void Age_WithNothingOpen_StillListsAllFourBandsAtZero()
        {
            var buckets = DashboardCalculator.BuildSummary(new List<Incident>(), "x", Now).OpenAgeBuckets;

            Assert.Equal(4, buckets.Count);
            Assert.All(buckets, b => Assert.Equal(0, b.Count));
        }

        [Fact]
        public void Age_TheBandsAddUpToTheActiveCount()
        {
            var incidents = new List<Incident> { Active(1), Active(40), Active(90), Active(900), Active(5, IncidentStatus.Closed) };

            var summary = DashboardCalculator.BuildSummary(incidents, "x", Now);

            Assert.Equal(summary.ActiveIncidents, summary.OpenAgeBuckets.Sum(b => b.Count));
        }

        // ---- the SLA compliance trend ----

        // Resolved: Tue 11-17 00:00 (Met), Thu 11-19 10:00 (Met), Fri 11-20 02:00 (Breached: took 11h of a 4h promise).
        private static List<Incident> ComplianceFixture() => new()
        {
            Resolved(IncidentPriority.Low, 48, 13, 84),
            Resolved(IncidentPriority.High, 8, 5, 26),
            Resolved(IncidentPriority.Critical, 4, 11, 10)
        };

        [Fact]
        public void Compliance_HasOneEntryPerDay_AndLeavesGapsWhereNothingWasResolved()
        {
            var trend = Stats(ComplianceFixture()).SlaComplianceTrend;

            Assert.Equal(7, trend.Count);
            Assert.Equal(new DateOnly(2026, 11, 14), trend[0].Date);
            Assert.Equal(new DateOnly(2026, 11, 20), trend[6].Date);

            // Nothing resolved in the week up to the 14th, 15th or 16th: no point, not a false 0%.
            Assert.All(trend.Take(3), d => Assert.Null(d.Percent));
            Assert.All(trend.Take(3), d => Assert.Equal(0, d.Resolved));
        }

        [Fact]
        public void Compliance_IsTheShareThatMetTheirSla_OverTheWeekUpToEachDay()
        {
            var trend = Stats(ComplianceFixture()).SlaComplianceTrend;

            Assert.Equal(new double?[] { null, null, null, 100, 100, 100, 66.7 }, trend.Select(d => d.Percent));
            Assert.Equal(new[] { 0, 0, 0, 1, 1, 2, 3 }, trend.Select(d => d.Resolved));
        }

        [Fact]
        public void Compliance_ARollingWeekForgetsOldResolutions()
        {
            // Resolved 10 days ago and late; then one resolved yesterday on time.
            var incidents = new List<Incident>
            {
                Resolved(IncidentPriority.Critical, 4, 11, 240),
                Resolved(IncidentPriority.High, 8, 5, 24)
            };

            var trend = Stats(incidents, days: 3).SlaComplianceTrend;   // 11-18, 11-19, 11-20

            // The late one (11-10) is outside every window shown, so only the on-time one counts.
            Assert.Equal(new double?[] { null, 100, 100 }, trend.Select(d => d.Percent));
        }

        [Fact]
        public void Compliance_ResolutionsBeforeThePeriodStillCountInTheEarlyDays()
        {
            // Resolved 11-13 (the day before a 3-day period starts) and late. The week up to 11-18 still includes it.
            var incidents = new List<Incident> { Resolved(IncidentPriority.Critical, 4, 11, 168 - 12 + 12) };

            var trend = Stats(incidents, days: 3).SlaComplianceTrend;

            Assert.Equal(0.0, trend[0].Percent);
            Assert.Equal(1, trend[0].Resolved);
        }

        [Fact]
        public void Compliance_IgnoresResolvedIncidentsWithNoSla()
        {
            var noSla = Resolved(IncidentPriority.Medium, 24, 5, 20);
            noSla.SlaTargetHours = null;   // no hours were promised, so there is nothing to measure against

            var trend = Stats(new List<Incident> { noSla }).SlaComplianceTrend;

            Assert.All(trend, d => Assert.Null(d.Percent));
        }

        [Fact]
        public void Compliance_AReopenedIncidentHasNoResolvedTime_SoItIsLeftOut()
        {
            var reopened = Resolved(IncidentPriority.Critical, 4, 11, 20);
            reopened.ResolvedAtUtc = null;
            reopened.Status = IncidentStatus.InProgress;

            var trend = Stats(new List<Incident> { reopened }).SlaComplianceTrend;

            Assert.All(trend, d => Assert.Null(d.Percent));
        }

        [Fact]
        public void Compliance_WithNoIncidents_IsAllGaps()
        {
            var trend = Stats(new List<Incident>()).SlaComplianceTrend;

            Assert.Equal(7, trend.Count);
            Assert.All(trend, d => Assert.Null(d.Percent));
        }

        // ---- time to resolve, by priority ----

        [Fact]
        public void ResolutionByPriority_ListsAllFourPriorities_MostUrgentFirst()
        {
            var result = Stats(new List<Incident>()).ResolutionByPriority;

            Assert.Equal(new[] { "Critical", "High", "Medium", "Low" }, result.Select(r => r.Priority));
            Assert.All(result, r => Assert.Null(r.AverageHours));
            Assert.All(result, r => Assert.Equal(0, r.Count));
        }

        [Fact]
        public void ResolutionByPriority_AveragesCreationToResolution_PerPriority()
        {
            var incidents = new List<Incident>
            {
                Resolved(IncidentPriority.Critical, 4, 2, 10),
                Resolved(IncidentPriority.Critical, 4, 4, 20),    // Critical: (2 + 4) / 2 = 3
                Resolved(IncidentPriority.High, 8, 5.26, 26),     // High: 5.26 -> 5.3 (rounded to one decimal)
                Resolved(IncidentPriority.Low, 48, 13, 84)        // Medium: none
            };

            var result = Stats(incidents).ResolutionByPriority;

            Assert.Equal(new double?[] { 3.0, 5.3, null, 13.0 }, result.Select(r => r.AverageHours));
            Assert.Equal(new[] { 2, 1, 0, 1 }, result.Select(r => r.Count));
        }

        [Fact]
        public void ResolutionByPriority_OnlyCountsWhatWasResolvedInsideThePeriod()
        {
            var incidents = new List<Incident>
            {
                Resolved(IncidentPriority.High, 8, 6, 26),        // yesterday: inside a 7-day period
                Resolved(IncidentPriority.High, 8, 100, 24 * 20)  // 20 days ago: outside
            };

            var result = Stats(incidents).ResolutionByPriority;

            Assert.Equal(6.0, result.Single(r => r.Priority == "High").AverageHours);
            Assert.Equal(1, result.Single(r => r.Priority == "High").Count);
        }

        [Fact]
        public void ResolutionByPriority_LeavesOutIncidentsThatNeverGotAPriority()
        {
            var unprioritized = Resolved(IncidentPriority.High, 8, 6, 26);
            unprioritized.Priority = null;

            var result = Stats(new List<Incident> { unprioritized }).ResolutionByPriority;

            Assert.All(result, r => Assert.Equal(0, r.Count));
        }
    }
}
