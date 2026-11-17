using System.Net;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class DashboardApiTests
    {
        private readonly ApiFactory _factory;

        public DashboardApiTests(ApiFactory factory) => _factory = factory;

        /// <summary>
        /// Four incidents, all raised by one reporter and (except the first) handled by one assignee:
        /// 1 unassigned, 2 High and in progress, 3 Critical and resolved, 4 Medium and on hold.
        /// Then 90 minutes pass.
        /// </summary>
        private async Task<(Scenario S, DateOnly CreatedOn)> BuildAsync()
        {
            var s = new Scenario
            {
                Reporter = await _factory.CreateUserAsync(),
                Manager = await _factory.CreateUserAsync(RoleNames.Manager),
                Assignee = await _factory.CreateUserAsync()
            };
            var categories = (await (await s.Reporter.Client.GetAsync("/api/categories")).ReadJsonAsync()).EnumerateArray().ToList();
            var hardware = categories.First(c => c.Str("name") == "Hardware").GetGuid("id");

            await s.Reporter.ReportIncidentAsync("first, unassigned", categoryId: hardware);

            var second = await s.Reporter.ReportIncidentAsync("second", categoryId: hardware);
            second = await s.Manager.AssignAsync(second, s.Assignee, "High");
            await s.Assignee.SetStatusAsync(second, "InProgress").ExpectAsync(HttpStatusCode.OK);

            var third = await s.Reporter.ReportIncidentAsync("third");
            third = await s.Manager.AssignAsync(third, s.Assignee, "Critical");
            await s.Assignee.SetStatusAsync(third, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(third, "Resolved").ExpectAsync(HttpStatusCode.OK);

            var fourth = await s.Reporter.ReportIncidentAsync("fourth");
            fourth = await s.Manager.AssignAsync(fourth, s.Assignee, "Medium");
            await s.Assignee.SetStatusAsync(fourth, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(fourth, "OnHold", "waiting").ExpectAsync(HttpStatusCode.OK);

            var createdOn = DateOnly.FromDateTime(_factory.Time.GetUtcNow().UtcDateTime);
            _factory.Time.Advance(TimeSpan.FromMinutes(90));

            return (s, createdOn);
        }

        private static int Count(JsonElement element, string property) => element.GetProperty(property).GetInt32();

        private static int Named(JsonElement list, string name) =>
            list.EnumerateArray().Single(i => i.Str("name") == name).GetProperty("count").GetInt32();

        private static int OnDay(JsonElement series, DateOnly day) =>
            series.EnumerateArray().Single(d => d.Str("date") == day.ToString("yyyy-MM-dd")).GetProperty("count").GetInt32();

        private static async Task<JsonElement> GetAsync(TestUser who, string url) =>
            await (await who.Client.GetAsync(url).ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();

        // ---- summary ----

        [Fact]
        public async Task Summary_CountsEverythingTheReporterCanSee()
        {
            var (s, _) = await BuildAsync();

            var summary = await GetAsync(s.Reporter, "/api/dashboard/summary");

            Assert.Equal(4, Count(summary, "totalIncidents"));
            Assert.Equal(3, Count(summary, "activeIncidents"));      // not the resolved one
            Assert.Equal(0, Count(summary, "assignedToMe"));          // the reporter is not an assignee
            Assert.Equal(1, Count(summary, "unassigned"));
            Assert.Equal(90, summary.GetProperty("oldestUnassignedWaitingMinutes").GetInt64());

            var sla = summary.GetProperty("sla");
            Assert.Equal(1, Count(sla, "notStarted"));
            Assert.Equal(1, Count(sla, "onTrack"));
            Assert.Equal(1, Count(sla, "paused"));
            Assert.Equal(1, Count(sla, "met"));
            Assert.Equal(0, Count(sla, "breached"));
            Assert.Equal(0, Count(sla, "atRisk"));
            Assert.Equal(0, Count(sla, "none"));
        }

        [Fact]
        public async Task Summary_ForTheAssignee_CountsTheirOwnWorkload()
        {
            var (s, _) = await BuildAsync();

            var summary = await GetAsync(s.Assignee, "/api/dashboard/summary");

            Assert.Equal(3, Count(summary, "totalIncidents"));        // second, third, fourth
            Assert.Equal(2, Count(summary, "activeIncidents"));       // second and fourth
            Assert.Equal(2, Count(summary, "assignedToMe"));
            Assert.Equal(0, Count(summary, "unassigned"));
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("oldestUnassignedWaitingMinutes").ValueKind);
        }

        [Fact]
        public async Task Summary_AgreesWithTheIncidentListForEverySlaState()
        {
            var (s, _) = await BuildAsync();
            _factory.Time.Advance(TimeSpan.FromHours(7));            // push the High one close to late, for variety

            var sla = (await GetAsync(s.Reporter, "/api/dashboard/summary")).GetProperty("sla");

            foreach (var (state, key) in new[]
            {
                ("NotStarted", "notStarted"), ("OnTrack", "onTrack"), ("AtRisk", "atRisk"),
                ("Paused", "paused"), ("Breached", "breached"), ("Met", "met")
            })
            {
                var list = await s.Reporter.ListIncidentsAsync($"slaState={state}");
                Assert.Equal(Count(sla, key), list.GetProperty("totalCount").GetInt32());
            }
        }

        [Fact]
        public async Task Summary_UnassignedMatchesTheUnassignedFilter()
        {
            var (s, _) = await BuildAsync();

            var summary = await GetAsync(s.Reporter, "/api/dashboard/summary");
            var list = await s.Reporter.ListIncidentsAsync("unassigned=true");

            Assert.Equal(Count(summary, "unassigned"), list.GetProperty("totalCount").GetInt32());
        }

        [Fact]
        public async Task Summary_ForSomeoneWithNothing_IsAllZero()
        {
            var nobody = await _factory.CreateUserAsync();

            var summary = await GetAsync(nobody, "/api/dashboard/summary");

            Assert.Equal(0, Count(summary, "totalIncidents"));
            Assert.Equal(0, Count(summary, "unassigned"));
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("oldestUnassignedWaitingMinutes").ValueKind);
        }

        // ---- statistics ----

        [Fact]
        public async Task Statistics_BreakDownByStatusPriorityAndCategory()
        {
            var (s, _) = await BuildAsync();

            var stats = await GetAsync(s.Reporter, "/api/dashboard/incident-statistics?days=7");

            var byStatus = stats.GetProperty("byStatus");
            Assert.Equal(new[] { "Open", "InProgress", "OnHold", "Resolved", "Closed" },
                byStatus.EnumerateArray().Select(i => i.Str("name")));
            Assert.Equal(1, Named(byStatus, "Open"));
            Assert.Equal(1, Named(byStatus, "InProgress"));
            Assert.Equal(1, Named(byStatus, "OnHold"));
            Assert.Equal(1, Named(byStatus, "Resolved"));
            Assert.Equal(0, Named(byStatus, "Closed"));

            var byPriority = stats.GetProperty("byPriority");
            Assert.Equal(new[] { "Critical", "High", "Medium", "Low", "Unprioritized" },
                byPriority.EnumerateArray().Select(i => i.Str("name")));
            Assert.Equal(1, Named(byPriority, "Critical"));
            Assert.Equal(1, Named(byPriority, "High"));
            Assert.Equal(1, Named(byPriority, "Medium"));
            Assert.Equal(0, Named(byPriority, "Low"));
            Assert.Equal(1, Named(byPriority, "Unprioritized"));

            var byCategory = stats.GetProperty("byCategory");
            Assert.Equal(2, Named(byCategory, "Hardware"));
            Assert.Equal(2, Named(byCategory, "Uncategorized"));
        }

        [Fact]
        public async Task Statistics_ShowOneEntryPerDay_IncludingEmptyDays()
        {
            var (s, createdOn) = await BuildAsync();

            var stats = await GetAsync(s.Reporter, "/api/dashboard/incident-statistics?days=7");
            var created = stats.GetProperty("createdPerDay");
            var resolved = stats.GetProperty("resolvedPerDay");

            Assert.Equal(7, created.GetArrayLength());
            Assert.Equal(7, resolved.GetArrayLength());
            Assert.Equal(4, OnDay(created, createdOn));
            Assert.Equal(4, created.EnumerateArray().Sum(d => d.GetProperty("count").GetInt32()));
            Assert.Equal(1, OnDay(resolved, createdOn));          // the third incident, resolved straight away
        }

        [Fact]
        public async Task Statistics_AverageResolutionTimeAndSlaCompliance()
        {
            var (s, _) = await BuildAsync();

            var stats = await GetAsync(s.Reporter, "/api/dashboard/incident-statistics");

            Assert.Equal(0.0, stats.GetProperty("averageResolutionHours").GetDouble());   // resolved the instant it was raised
            Assert.Equal(100.0, stats.GetProperty("slaCompliancePercent").GetDouble());   // and so within its target
        }

        [Fact]
        public async Task Statistics_ForSomeoneWithNothing_HaveNoAveragesAndAllZeroDays()
        {
            var nobody = await _factory.CreateUserAsync();

            var stats = await GetAsync(nobody, "/api/dashboard/incident-statistics?days=5");

            Assert.Equal(JsonValueKind.Null, stats.GetProperty("averageResolutionHours").ValueKind);
            Assert.Equal(JsonValueKind.Null, stats.GetProperty("slaCompliancePercent").ValueKind);
            Assert.Equal(5, stats.GetProperty("createdPerDay").GetArrayLength());
            Assert.All(stats.GetProperty("createdPerDay").EnumerateArray(), d => Assert.Equal(0, d.GetProperty("count").GetInt32()));
        }

        [Fact]
        public async Task Statistics_TheDefaultPeriodIs30Days()
        {
            var nobody = await _factory.CreateUserAsync();

            var stats = await GetAsync(nobody, "/api/dashboard/incident-statistics");

            Assert.Equal(30, stats.GetProperty("days").GetInt32());
            Assert.Equal(30, stats.GetProperty("createdPerDay").GetArrayLength());
        }

        [Theory]
        [InlineData("days=0")]
        [InlineData("days=-1")]
        [InlineData("days=366")]
        [InlineData("days=abc")]
        public async Task Statistics_RejectAnInvalidPeriod(string query)
        {
            var nobody = await _factory.CreateUserAsync();

            await nobody.Client.GetAsync($"/api/dashboard/incident-statistics?{query}").ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Statistics_TheLongestAllowedPeriodWorks()
        {
            var nobody = await _factory.CreateUserAsync();

            var stats = await GetAsync(nobody, "/api/dashboard/incident-statistics?days=365");

            Assert.Equal(365, stats.GetProperty("createdPerDay").GetArrayLength());
        }

        // ---- the analytics charts ----

        [Fact]
        public async Task TheSummary_SaysHowOldTheOpenIncidentsAre_InFourBands()
        {
            var (s, _) = await BuildAsync();

            var buckets = (await GetAsync(s.Reporter, "/api/dashboard/summary")).GetProperty("openAgeBuckets");

            Assert.Equal(new[] { "Under1Day", "1To3Days", "3To7Days", "Over7Days" }, buckets.EnumerateArray().Select(b => b.Str("name")));
            // Three of the four incidents are still open (the third was resolved), and all of them were raised just now.
            Assert.Equal(3, Named(buckets, "Under1Day"));
            Assert.Equal(0, Named(buckets, "Over7Days"));
        }

        [Fact]
        public async Task TheStatistics_ShowHowLongEachPriorityTakesToResolve()
        {
            var (s, _) = await BuildAsync();

            var rows = (await GetAsync(s.Reporter, "/api/dashboard/incident-statistics")).GetProperty("resolutionByPriority");

            Assert.Equal(new[] { "Critical", "High", "Medium", "Low" }, rows.EnumerateArray().Select(r => r.Str("priority")));

            var critical = rows.EnumerateArray().Single(r => r.Str("priority") == "Critical");
            Assert.Equal(1, critical.GetProperty("count").GetInt32());                         // the third incident
            Assert.True(critical.GetProperty("averageHours").GetDouble() >= 0);

            var high = rows.EnumerateArray().Single(r => r.Str("priority") == "High");
            Assert.Equal(0, high.GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKind.Null, high.GetProperty("averageHours").ValueKind);      // none resolved: no average
        }

        [Fact]
        public async Task TheStatistics_GiveOneComplianceEntryPerDay_AndTodayCountsTheOnTimeResolution()
        {
            var (s, createdOn) = await BuildAsync();

            var trend = (await GetAsync(s.Reporter, "/api/dashboard/incident-statistics?days=7")).GetProperty("slaComplianceTrend");

            Assert.Equal(7, trend.GetArrayLength());
            var today = trend.EnumerateArray().Single(d => d.Str("date") == createdOn.ToString("yyyy-MM-dd"));
            Assert.Equal(1, today.GetProperty("resolved").GetInt32());
            Assert.Equal(100.0, today.GetProperty("percent").GetDouble());                     // resolved in minutes, within hours

            // The days before had nothing resolved in their week: a gap, not 0%.
            var first = trend.EnumerateArray().First();
            Assert.Equal(JsonValueKind.Null, first.GetProperty("percent").ValueKind);
        }

        [Fact]
        public async Task TheAnalytics_AreSafeForAnEmployeeWithNoIncidents()
        {
            var nobody = await _factory.CreateUserAsync();

            var summary = await GetAsync(nobody, "/api/dashboard/summary");
            var stats = await GetAsync(nobody, "/api/dashboard/incident-statistics");

            Assert.Equal(4, summary.GetProperty("openAgeBuckets").GetArrayLength());
            Assert.Equal(4, stats.GetProperty("resolutionByPriority").GetArrayLength());
            Assert.Equal(30, stats.GetProperty("slaComplianceTrend").GetArrayLength());
        }

        // ---- who sees the workload table ----

        [Fact]
        public async Task TheWorkloadTable_IsForManagersAndAdminsOnly()
        {
            var (s, _) = await BuildAsync();
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            Assert.Equal(JsonValueKind.Null, (await GetAsync(s.Reporter, "/api/dashboard/incident-statistics")).GetProperty("workload").ValueKind);
            Assert.Equal(JsonValueKind.Null, (await GetAsync(s.Assignee, "/api/dashboard/incident-statistics")).GetProperty("workload").ValueKind);

            foreach (var viewer in new[] { s.Manager, admin })
            {
                var workload = (await GetAsync(viewer, "/api/dashboard/incident-statistics")).GetProperty("workload");
                var row = workload.EnumerateArray().Single(w => w.Str("email") == s.Assignee.Email);

                Assert.Equal(2, row.GetProperty("activeIncidents").GetInt32());     // second and fourth
                Assert.Equal(0, row.GetProperty("breachedIncidents").GetInt32());
            }
        }

        [Fact]
        public async Task TheWorkloadTable_CountsLateIncidents()
        {
            var (s, _) = await BuildAsync();
            _factory.Time.Advance(TimeSpan.FromHours(10));           // the High one (8 hours) is now late

            var workload = (await GetAsync(s.Manager, "/api/dashboard/incident-statistics")).GetProperty("workload");
            var row = workload.EnumerateArray().Single(w => w.Str("email") == s.Assignee.Email);

            Assert.Equal(1, row.GetProperty("breachedIncidents").GetInt32());
        }

        [Fact]
        public async Task TheDashboardNeedsALogin()
        {
            var anonymous = _factory.CreateClient();

            await anonymous.GetAsync("/api/dashboard/summary").ExpectAsync(HttpStatusCode.Unauthorized);
            await anonymous.GetAsync("/api/dashboard/incident-statistics").ExpectAsync(HttpStatusCode.Unauthorized);
        }
    }
}
