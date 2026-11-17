using ResolveIQ.Application.Dashboard;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Tests.Dashboard
{
    public class DashboardCalculatorTests
    {
        // Fixed "now": Friday 2026-11-20 12:00 UTC.
        private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static readonly Dictionary<IncidentPriority, int> Targets = new()
        {
            [IncidentPriority.Critical] = 4,
            [IncidentPriority.High] = 8,
            [IncidentPriority.Medium] = 24,
            [IncidentPriority.Low] = 48
        };

        private static readonly Guid Hardware = Guid.NewGuid();
        private static readonly Guid Network = Guid.NewGuid();
        private static readonly Guid Software = Guid.NewGuid();

        private static readonly Dictionary<Guid, string> CategoryNames = new()
        {
            [Hardware] = "Hardware",
            [Network] = "Network",
            [Software] = "Software"
        };

        private static readonly Dictionary<string, string> Emails = new()
        {
            ["S"] = "sam@example.com",
            ["P"] = "priya@example.com"
        };

        private static DateTime Ago(double hours) => Now.AddHours(-hours);

        // Nine incidents. Their SLA states, worked out by hand:
        //   a, b unassigned -> NotStarted     c -> AtRisk     d -> Breached   e -> Paused
        //   f, g resolved on time -> Met      h resolved late -> Breached     i -> OnTrack
        private static List<Incident> UnstampedFixture() => new()
        {
            new Incident { Status = IncidentStatus.Open, CategoryId = Hardware, CreatedAtUtc = Ago(5) },
            new Incident { Status = IncidentStatus.Open, CategoryId = Hardware, CreatedAtUtc = Ago(2) },
            new Incident
            {
                Status = IncidentStatus.InProgress, Priority = IncidentPriority.High, AssignedToUserId = "S",
                CategoryId = Network, CreatedAtUtc = Ago(9), SlaStartedAtUtc = Ago(7)
            },
            new Incident
            {
                Status = IncidentStatus.InProgress, Priority = IncidentPriority.Critical, AssignedToUserId = "S",
                CreatedAtUtc = Ago(6), SlaStartedAtUtc = Ago(5)
            },
            new Incident
            {
                Status = IncidentStatus.OnHold, Priority = IncidentPriority.Medium, AssignedToUserId = "P",
                CategoryId = Network, CreatedAtUtc = Ago(11), SlaStartedAtUtc = Ago(10), SlaPausedAtUtc = Ago(1)
            },
            new Incident
            {
                Status = IncidentStatus.Resolved, Priority = IncidentPriority.High, AssignedToUserId = "P",
                CategoryId = Hardware, CreatedAtUtc = Ago(31), SlaStartedAtUtc = Ago(30), ResolvedAtUtc = Ago(26)
            },
            new Incident
            {
                Status = IncidentStatus.Closed, Priority = IncidentPriority.Low, AssignedToUserId = "S",
                CreatedAtUtc = Ago(97), SlaStartedAtUtc = Ago(96), ResolvedAtUtc = Ago(84)
            },
            new Incident
            {
                Status = IncidentStatus.Resolved, Priority = IncidentPriority.Critical, AssignedToUserId = "P",
                CategoryId = Software, CreatedAtUtc = Ago(21), SlaStartedAtUtc = Ago(20), ResolvedAtUtc = Ago(10)
            },
            new Incident
            {
                Status = IncidentStatus.InProgress, Priority = IncidentPriority.Medium, AssignedToUserId = "S",
                CategoryId = Hardware, CreatedAtUtc = Ago(2), SlaStartedAtUtc = Ago(1)
            }
        };

        // Every incident whose clock has started was promised the hours of its priority at that moment.
        private static List<Incident> Fixture()
        {
            var incidents = UnstampedFixture();

            foreach (var incident in incidents)
            {
                if (incident.SlaStartedAtUtc is not null && incident.Priority is IncidentPriority priority)
                {
                    incident.SlaTargetHours = Targets[priority];
                }
            }

            return incidents;
        }

        private static int CountOf(IEnumerable<ResolveIQ.Application.DTOs.Dashboard.CountItem> items, string name) =>
            items.Single(c => c.Name == name).Count;

        // ---- summary ----

        [Fact]
        public void Summary_CountsTotalActiveAndAssignedToMe()
        {
            var summary = DashboardCalculator.BuildSummary(Fixture(), "S", Now);

            Assert.Equal(9, summary.TotalIncidents);
            Assert.Equal(6, summary.ActiveIncidents);   // a, b, c, d, e, i (f, g, h are resolved or closed)
            Assert.Equal(3, summary.AssignedToMe);       // Sam's active ones: c, d, i (g is closed)
        }

        [Fact]
        public void Summary_ReportsUnassignedAndHowLongTheOldestHasWaited()
        {
            var summary = DashboardCalculator.BuildSummary(Fixture(), "S", Now);

            Assert.Equal(2, summary.Unassigned);
            Assert.Equal(300, summary.OldestUnassignedWaitingMinutes); // incident a: 5 hours
        }

        [Fact]
        public void Summary_CountsEachSlaState_AndTheyAddUpToTheTotal()
        {
            var sla = DashboardCalculator.BuildSummary(Fixture(), "S", Now).Sla;

            Assert.Equal(2, sla.NotStarted);
            Assert.Equal(1, sla.OnTrack);
            Assert.Equal(1, sla.AtRisk);
            Assert.Equal(1, sla.Paused);
            Assert.Equal(2, sla.Breached);
            Assert.Equal(2, sla.Met);
            Assert.Equal(0, sla.None);
            Assert.Equal(9, sla.NotStarted + sla.OnTrack + sla.AtRisk + sla.Paused + sla.Breached + sla.Met + sla.None);
        }

        [Fact]
        public void Summary_ForSomeoneWithNoIncidents_IsAllZero()
        {
            var summary = DashboardCalculator.BuildSummary(new List<Incident>(), "S", Now);

            Assert.Equal(0, summary.TotalIncidents);
            Assert.Equal(0, summary.ActiveIncidents);
            Assert.Equal(0, summary.Unassigned);
            Assert.Null(summary.OldestUnassignedWaitingMinutes);
            Assert.Equal(0, summary.Sla.OnTrack);
        }

        [Fact]
        public void Summary_WhenNoHoursWerePromised_CountsStartedIncidentsAsNone()
        {
            var summary = DashboardCalculator.BuildSummary(UnstampedFixture(), "S", Now);

            Assert.Equal(2, summary.Sla.NotStarted);  // still unassigned
            Assert.Equal(7, summary.Sla.None);        // started, but promised no hours: nothing to measure against
        }

        // ---- statistics ----

        [Fact]
        public void Statistics_BreaksDownByStatus_InWorkflowOrder_WithZeros()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            Assert.Equal(new[] { "Open", "InProgress", "OnHold", "Resolved", "Closed" }, stats.ByStatus.Select(s => s.Name));
            Assert.Equal(new[] { 2, 3, 1, 2, 1 }, stats.ByStatus.Select(s => s.Count));
        }

        [Fact]
        public void Statistics_BreaksDownByPriority_HighestFirst_WithAnUnprioritizedBucket()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            Assert.Equal(new[] { "Critical", "High", "Medium", "Low", "Unprioritized" }, stats.ByPriority.Select(p => p.Name));
            Assert.Equal(new[] { 2, 2, 2, 1, 2 }, stats.ByPriority.Select(p => p.Count));
        }

        [Fact]
        public void Statistics_BreaksDownByCategory_BiggestFirst_WithAnUncategorizedBucket()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            Assert.Equal(4, CountOf(stats.ByCategory, "Hardware"));
            Assert.Equal(2, CountOf(stats.ByCategory, "Network"));
            Assert.Equal(1, CountOf(stats.ByCategory, "Software"));
            Assert.Equal(2, CountOf(stats.ByCategory, "Uncategorized"));
            Assert.Equal("Hardware", stats.ByCategory[0].Name);
            Assert.Equal(9, stats.ByCategory.Sum(c => c.Count));
        }

        [Fact]
        public void Statistics_CreatedPerDay_HasOneEntryPerDay_IncludingEmptyDays()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            Assert.Equal(7, stats.CreatedPerDay.Count);
            Assert.Equal(new DateOnly(2026, 11, 14), stats.From);
            Assert.Equal(new DateOnly(2026, 11, 20), stats.To);
            Assert.Equal(new[] { 0, 0, 1, 0, 0, 2, 6 }, stats.CreatedPerDay.Select(d => d.Count));
        }

        [Fact]
        public void Statistics_ResolvedPerDay_CountsByTheDayItWasResolved()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            // Nov 17: g, Nov 19: f, Nov 20: h
            Assert.Equal(new[] { 0, 0, 0, 1, 0, 1, 1 }, stats.ResolvedPerDay.Select(d => d.Count));
        }

        [Fact]
        public void Statistics_AverageResolutionTime_IsTheMeanOfCreationToResolution()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            // f: 5 h, g: 13 h, h: 11 h  ->  29 / 3 = 9.67
            Assert.Equal(9.7, stats.AverageResolutionHours);
        }

        [Fact]
        public void Statistics_SlaCompliance_IsMetOutOfMetPlusBreached()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            // f and g met, h breached -> 2 of 3
            Assert.Equal(66.7, stats.SlaCompliancePercent);
        }

        [Fact]
        public void Statistics_ANarrowerPeriodOnlyCountsWhatWasResolvedInsideIt()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 1, Now);

            Assert.Single(stats.CreatedPerDay);
            Assert.Equal(6, stats.CreatedPerDay[0].Count);
            Assert.Equal(11.0, stats.AverageResolutionHours);   // only h, resolved today
            Assert.Equal(0.0, stats.SlaCompliancePercent);       // h was late
        }

        [Fact]
        public void Statistics_WorkloadListsActiveIncidentsPerPerson_BusiestFirst()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, true, 7, Now);

            Assert.NotNull(stats.Workload);
            Assert.Equal(2, stats.Workload!.Count);

            Assert.Equal("sam@example.com", stats.Workload[0].Email);
            Assert.Equal(3, stats.Workload[0].ActiveIncidents);
            Assert.Equal(1, stats.Workload[0].BreachedIncidents);   // d

            Assert.Equal("priya@example.com", stats.Workload[1].Email);
            Assert.Equal(1, stats.Workload[1].ActiveIncidents);      // e (h is resolved, so not active)
            Assert.Equal(0, stats.Workload[1].BreachedIncidents);
        }

        [Fact]
        public void Statistics_WorkloadIsLeftOutWhenNotAllowed()
        {
            var stats = DashboardCalculator.BuildStatistics(Fixture(), CategoryNames, Emails, false, 7, Now);

            Assert.Null(stats.Workload);
        }

        [Fact]
        public void Statistics_WithNoIncidents_GivesZerosAndEmptyAverages()
        {
            var stats = DashboardCalculator.BuildStatistics(
                new List<Incident>(), CategoryNames, Emails, true, 5, Now);

            Assert.Equal(5, stats.CreatedPerDay.Count);
            Assert.All(stats.CreatedPerDay, d => Assert.Equal(0, d.Count));
            Assert.All(stats.ByStatus, s => Assert.Equal(0, s.Count));
            Assert.Null(stats.AverageResolutionHours);
            Assert.Null(stats.SlaCompliancePercent);
            Assert.Empty(stats.Workload!);
        }
    }
}
