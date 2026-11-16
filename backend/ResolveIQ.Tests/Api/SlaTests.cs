using System.Net;
using System.Net.Http.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class SlaClockTests
    {
        private readonly ApiFactory _factory;

        public SlaClockTests(ApiFactory factory) => _factory = factory;

        private void Hours(double hours) => _factory.Time.Advance(TimeSpan.FromHours(hours));

        private static async Task<string> SlaStateAsync(Scenario s) => (await s.RefreshAsync()).Str("slaState");

        [Fact]
        public async Task AnUnassignedIncidentHasNoClock_HoweverLongItWaits()
        {
            var s = await Scenario.UnassignedAsync(_factory);

            Hours(500);

            var incident = await s.RefreshAsync();
            Assert.Equal("NotStarted", incident.Str("slaState"));
            Assert.Null(incident.StrOrNull("slaDueAtUtc"));
        }

        [Fact]
        public async Task TheStateMovesFromOnTrackToAtRiskToBreached_AsTimePasses()
        {
            // High = 8 hours. "At risk" begins with 25% (2 hours) left.
            var s = await Scenario.AssignedAsync(_factory, "High");

            Hours(5);
            Assert.Equal("OnTrack", await SlaStateAsync(s));        // 3 hours left

            Hours(1.5);
            Assert.Equal("AtRisk", await SlaStateAsync(s));         // 1.5 hours left

            Hours(1.4);
            Assert.Equal("AtRisk", await SlaStateAsync(s));         // 6 minutes left

            Hours(0.2);
            Assert.Equal("Breached", await SlaStateAsync(s));       // 6 minutes late
        }

        [Theory]
        [InlineData("Critical", 4)]
        [InlineData("High", 8)]
        [InlineData("Medium", 24)]
        [InlineData("Low", 48)]
        public async Task EachPriorityGetsItsOwnTarget(string priority, int hours)
        {
            var s = await Scenario.AssignedAsync(_factory, priority);

            Assert.Equal(TimeSpan.FromHours(hours), s.Incident.Time("slaDueAtUtc") - s.Incident.Time("slaStartedAtUtc"));
        }

        [Fact]
        public async Task OnHold_PausesTheClock_AndTheDeadlineMovesOutByTheTimeSpentOnHold()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");
            var start = s.Incident.Time("slaStartedAtUtc");

            Hours(2);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "Waiting for a part").ExpectAsync(HttpStatusCode.OK);

            Hours(20);
            var paused = await s.RefreshAsync();
            Assert.Equal("Paused", paused.Str("slaState"));
            Assert.Null(paused.StrOrNull("slaDueAtUtc"));            // no deadline while paused

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            var resumed = await s.RefreshAsync();

            // 8 hours allowed + 20 hours on hold. And 2 hours were already used, so 6 are left.
            Assert.Equal(start.AddHours(28), resumed.Time("slaDueAtUtc"));
            Assert.Equal("OnTrack", resumed.Str("slaState"));
        }

        [Fact]
        public async Task SeveralHoldsAddUp()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");
            var start = s.Incident.Time("slaStartedAtUtc");
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            foreach (var holdHours in new[] { 3.0, 5.0 })
            {
                await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "waiting").ExpectAsync(HttpStatusCode.OK);
                Hours(holdHours);
                await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            }

            Assert.Equal(start.AddHours(8 + 3 + 5), (await s.RefreshAsync()).Time("slaDueAtUtc"));
        }

        [Fact]
        public async Task AnIncidentThatWasAlreadyLateWhenPutOnHoldStaysBreached()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            Hours(9);                                                // past the 8-hour deadline
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "waiting").ExpectAsync(HttpStatusCode.OK);

            Assert.Equal("Breached", await SlaStateAsync(s));
        }

        [Fact]
        public async Task ResolvingInTime_IsMet_AndStaysMetForever()
        {
            var s = await Scenario.AssignedAsync(_factory, "Critical");   // 4 hours

            Hours(1);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("Met", await SlaStateAsync(s));

            Hours(1000);
            Assert.Equal("Met", await SlaStateAsync(s));

            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("Met", await SlaStateAsync(s));
        }

        [Fact]
        public async Task ResolvingLate_IsBreached_AndStaysBreached()
        {
            var s = await Scenario.AssignedAsync(_factory, "Critical");   // 4 hours

            Hours(5);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);

            Hours(1000);
            Assert.Equal("Breached", await SlaStateAsync(s));
        }

        [Fact]
        public async Task TimeSpentOnHoldCanTurnALateResolutionIntoAnOnTimeOne()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");   // 8 hours
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "waiting").ExpectAsync(HttpStatusCode.OK);
            Hours(6);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            Hours(7);                                                  // 13 hours elapsed, but only 7 of work time
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);

            Assert.Equal("Met", await SlaStateAsync(s));
        }

        [Fact]
        public async Task Reopening_DoesNotCountTheTimeItSpentResolved()
        {
            var s = await Scenario.AssignedAsync(_factory, "Critical");   // 4 hours
            var start = s.Incident.Time("slaStartedAtUtc");

            Hours(1);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);   // 1 hour used, 3 left

            Hours(5);                                                  // sits resolved for 5 hours
            await s.Reporter.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            var reopened = await s.RefreshAsync();
            Assert.Equal(start.AddHours(4 + 5), reopened.Time("slaDueAtUtc"));
            Assert.Equal("OnTrack", reopened.Str("slaState"));         // 3 hours left again

            Hours(2.5);
            Assert.Equal("AtRisk", await SlaStateAsync(s));            // 30 minutes left of 4 hours
        }

        [Fact]
        public async Task ChangingThePriority_RestartsTheClock_AndTheHistoryRemembersWhatTheOldClockSaid()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");
            var originalStart = s.Incident.Time("slaStartedAtUtc");

            Hours(10);                                                 // High is 8 hours, so this is late
            Assert.Equal("Breached", await SlaStateAsync(s));

            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Critical" }).ExpectAsync(HttpStatusCode.OK);

            var restarted = await s.RefreshAsync();
            Assert.Equal(originalStart.AddHours(10), restarted.Time("slaStartedAtUtc"));
            Assert.Equal(TimeSpan.FromHours(4), restarted.Time("slaDueAtUtc") - restarted.Time("slaStartedAtUtc"));
            Assert.Equal("OnTrack", restarted.Str("slaState"));

            // The breach cannot be quietly erased: the history says what state the old clock was in.
            Assert.Contains((await s.HistoryAsync()).EnumerateArray(),
                r => r.Str("fieldChanged") == "SlaClock" && r.Str("oldValue") == "Breached" && r.Str("newValue") == "Restarted");
        }

        [Fact]
        public async Task ChangingThePriorityWhileOnHold_RestartsTheClock_ButKeepsItPaused()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "waiting").ExpectAsync(HttpStatusCode.OK);

            Hours(3);
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Critical" }).ExpectAsync(HttpStatusCode.OK);

            Assert.Equal("Paused", await SlaStateAsync(s));

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("OnTrack", await SlaStateAsync(s));
        }

        [Fact]
        public async Task AnIncidentCreatedWithNoPriorityYet_ShowsNoSla_EvenAfterAssignmentSetsOne()
        {
            var s = await Scenario.UnassignedAsync(_factory);
            Assert.Null(s.Incident.StrOrNull("priority"));

            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee, "Low");

            Assert.Equal("Low", assigned.Str("priority"));
            Assert.NotEqual("NotStarted", assigned.Str("slaState"));
        }
    }

    [Collection(ApiCollection.Name)]
    public class SlaPolicyTests
    {
        private readonly ApiFactory _factory;

        public SlaPolicyTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TheStartingTargetsAreSeeded()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            var policies = (await (await manager.Client.GetAsync("/api/sla/policies").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync())
                .EnumerateArray()
                .ToDictionary(p => p.Str("priority"), p => p.GetProperty("resolutionTargetHours").GetInt32());

            Assert.Equal(4, policies["Critical"]);
            Assert.Equal(8, policies["High"]);
            Assert.Equal(24, policies["Medium"]);
            Assert.Equal(48, policies["Low"]);
        }

        [Fact]
        public async Task OnlyAdminsEditPolicies_AndTheValuesAreRangeChecked()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var employee = await _factory.CreateUserAsync();
            var body = new { resolutionTargetHours = 48, isActive = true };

            await employee.Client.PutAsJsonAsync("/api/sla/policies/Low", body).ExpectAsync(HttpStatusCode.Forbidden);
            await manager.Client.PutAsJsonAsync("/api/sla/policies/Low", body).ExpectAsync(HttpStatusCode.Forbidden);

            await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", new { resolutionTargetHours = 0, isActive = true }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", new { resolutionTargetHours = 721, isActive = true }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PutAsJsonAsync("/api/sla/policies/Banana", body).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PutAsJsonAsync("/api/sla/policies/99", body).ExpectAsync(HttpStatusCode.BadRequest);

            await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", body).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task EditingAPolicy_ChangesTheDeadlineOfIncidentsAlreadyRunning()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var s = await Scenario.AssignedAsync(_factory, "Critical");
            var start = s.Incident.Time("slaStartedAtUtc");

            try
            {
                await admin.Client.PutAsJsonAsync("/api/sla/policies/Critical", new { resolutionTargetHours = 10, isActive = true })
                    .ExpectAsync(HttpStatusCode.OK);

                Assert.Equal(start.AddHours(10), (await s.RefreshAsync()).Time("slaDueAtUtc"));
            }
            finally
            {
                await admin.Client.PutAsJsonAsync("/api/sla/policies/Critical", new { resolutionTargetHours = 4, isActive = true });
            }

            Assert.Equal(start.AddHours(4), (await s.RefreshAsync()).Time("slaDueAtUtc"));
        }

        [Fact]
        public async Task ADeactivatedPolicy_MeansNoSlaForThatPriority()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var s = await Scenario.AssignedAsync(_factory, "Low");

            try
            {
                await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", new { resolutionTargetHours = 48, isActive = false })
                    .ExpectAsync(HttpStatusCode.OK);

                var incident = await s.RefreshAsync();
                Assert.Equal("None", incident.Str("slaState"));
                Assert.Null(incident.StrOrNull("slaDueAtUtc"));
            }
            finally
            {
                await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", new { resolutionTargetHours = 48, isActive = true });
            }

            Assert.Equal("OnTrack", (await s.RefreshAsync()).Str("slaState"));
        }
    }
}
