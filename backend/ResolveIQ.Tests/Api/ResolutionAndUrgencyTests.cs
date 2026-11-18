using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// Marking an incident resolved takes a note on how it was fixed, which the reporter reads before confirming.
    /// And a reporter can say how urgent a problem feels, as a hint: the real priority still comes from a Manager.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class ResolutionAndUrgencyTests
    {
        private readonly ApiFactory _factory;

        public ResolutionAndUrgencyTests(ApiFactory factory) => _factory = factory;

        private sealed record Scene(TestUser Reporter, TestUser Manager, TestUser Assignee, JsonElement Incident);

        /// <summary>An incident that is assigned and being worked on.</summary>
        private async Task<Scene> InProgressAsync()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync();

            var incident = await reporter.ReportIncidentAsync();
            incident = await manager.AssignAsync(incident, assignee);
            await assignee.SetStatusAsync(incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            return new Scene(reporter, manager, assignee, incident);
        }

        private static Task<HttpResponseMessage> Resolve(TestUser who, JsonElement incident, string? notes) =>
            who.Client.PutAsJsonAsync($"/api/incidents/{incident.GetGuid("id")}/status", new { status = "Resolved", reason = notes });

        private static async Task<JsonElement> HistoryAsync(TestUser who, JsonElement incident) =>
            await (await who.Client.GetAsync($"/api/incidents/{incident.GetGuid("id")}/history").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();

        // ---- resolution notes ----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ResolvingNeedsNotes_AndWithoutThemTheIncidentStaysInProgress(string? notes)
        {
            var s = await InProgressAsync();

            var response = await Resolve(s.Assignee, s.Incident, notes).ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("Reason", out _));
            Assert.Equal("InProgress", (await s.Assignee.GetIncidentAsync(s.Incident)).Str("status"));
        }

        [Fact]
        public async Task TheNotesAreSavedTrimmed_AndShownToEveryoneWhoCanSeeTheIncident()
        {
            var s = await InProgressAsync();

            await Resolve(s.Assignee, s.Incident, "  Replaced the roller.  ").ExpectAsync(HttpStatusCode.OK);

            foreach (var viewer in new[] { s.Reporter, s.Assignee, s.Manager })
            {
                Assert.Equal("Replaced the roller.", (await viewer.GetIncidentAsync(s.Incident)).Str("resolutionNotes"));
            }
        }

        [Fact]
        public async Task TheNotesAreAlsoWrittenToTheHistory()
        {
            var s = await InProgressAsync();
            await Resolve(s.Assignee, s.Incident, "Replaced the roller.").ExpectAsync(HttpStatusCode.OK);

            var entry = (await HistoryAsync(s.Reporter, s.Incident)).EnumerateArray().Single(h => h.Str("fieldChanged") == "ResolutionNotes");

            Assert.Equal("Replaced the roller.", entry.Str("newValue"));
            Assert.Equal(s.Assignee.Email, entry.Str("changedBy"));
        }

        [Fact]
        public async Task TheNotesHaveALimit_OfTwoThousandCharacters()
        {
            var s = await InProgressAsync();

            await Resolve(s.Assignee, s.Incident, new string('x', 2001)).ExpectAsync(HttpStatusCode.BadRequest);
            await Resolve(s.Assignee, s.Incident, new string('x', 2000)).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AHoldReasonStillHasTheShorterLimit_OfFiveHundred()
        {
            var s = await InProgressAsync();
            var url = $"/api/incidents/{s.Incident.GetGuid("id")}/status";

            await s.Assignee.Client.PutAsJsonAsync(url, new { status = "OnHold", reason = new string('x', 501) }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Assignee.Client.PutAsJsonAsync(url, new { status = "OnHold", reason = new string('x', 500) }).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ANoteIsNotShownBeforeTheIncidentIsResolved()
        {
            var s = await InProgressAsync();

            Assert.Equal(JsonValueKind.Null, (await s.Reporter.GetIncidentAsync(s.Incident)).GetProperty("resolutionNotes").ValueKind);
        }

        [Fact]
        public async Task AManagerResolvingOnSomeonesBehalfNeedsNotesToo()
        {
            var s = await InProgressAsync();

            await Resolve(s.Manager, s.Incident, null).ExpectAsync(HttpStatusCode.BadRequest);
            await Resolve(s.Manager, s.Incident, "Fixed remotely.").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ReopeningClearsTheNotes_ButTheHistoryKeepsWhatWasWritten()
        {
            var s = await InProgressAsync();
            await Resolve(s.Assignee, s.Incident, "First attempt.").ExpectAsync(HttpStatusCode.OK);

            await s.Reporter.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);   // "not fixed"

            var reopened = await s.Reporter.GetIncidentAsync(s.Incident);
            Assert.Equal(JsonValueKind.Null, reopened.GetProperty("resolutionNotes").ValueKind);
            Assert.Equal(JsonValueKind.Null, reopened.GetProperty("resolvedAtUtc").ValueKind);

            var notes = (await HistoryAsync(s.Reporter, s.Incident)).EnumerateArray()
                .Where(h => h.Str("fieldChanged") == "ResolutionNotes").Select(h => h.Str("newValue"));
            Assert.Equal(new[] { "First attempt." }, notes);
        }

        [Fact]
        public async Task ResolvingAgainAfterAReopen_ShowsTheNewNotes_AndKeepsBothInTheHistory()
        {
            var s = await InProgressAsync();
            await Resolve(s.Assignee, s.Incident, "First attempt.").ExpectAsync(HttpStatusCode.OK);
            await s.Reporter.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            await Resolve(s.Assignee, s.Incident, "Second, proper fix.").ExpectAsync(HttpStatusCode.OK);

            Assert.Equal("Second, proper fix.", (await s.Reporter.GetIncidentAsync(s.Incident)).Str("resolutionNotes"));
            var notes = (await HistoryAsync(s.Reporter, s.Incident)).EnumerateArray()
                .Where(h => h.Str("fieldChanged") == "ResolutionNotes").Select(h => h.Str("newValue"));
            Assert.Equal(new[] { "First attempt.", "Second, proper fix." }, notes);
        }

        [Fact]
        public async Task ClosingRecordsWhenItWasClosed_AndTheNotesStayOnAClosedIncident()
        {
            var s = await InProgressAsync();
            await Resolve(s.Assignee, s.Incident, "Done.").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(JsonValueKind.Null, (await s.Reporter.GetIncidentAsync(s.Incident)).GetProperty("closedAtUtc").ValueKind);

            _factory.Time.Advance(TimeSpan.FromMinutes(5));
            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);

            var closed = await s.Reporter.GetIncidentAsync(s.Incident);
            Assert.Equal("Done.", closed.Str("resolutionNotes"));
            Assert.True(closed.Time("closedAtUtc") > closed.Time("resolvedAtUtc"));
        }

        [Fact]
        public async Task AResolvedIncidentSaysWhenItWillCloseByItself_AndOtherStatesDoNot()
        {
            var s = await InProgressAsync();
            Assert.Equal(JsonValueKind.Null, (await s.Reporter.GetIncidentAsync(s.Incident)).GetProperty("autoCloseAtUtc").ValueKind);

            await Resolve(s.Assignee, s.Incident, "Done.").ExpectAsync(HttpStatusCode.OK);

            var resolved = await s.Reporter.GetIncidentAsync(s.Incident);
            Assert.Equal(resolved.Time("resolvedAtUtc").AddDays(7), resolved.Time("autoCloseAtUtc"));
        }

        // ---- the reporter's urgency ----

        [Theory]
        [InlineData("Low")]
        [InlineData("Medium")]
        [InlineData("High")]
        [InlineData("Critical")]
        public async Task AReporterCanSayHowUrgentItIs(string urgency)
        {
            var reporter = await _factory.CreateUserAsync();

            var incident = await reporter.ReportIncidentAsync(urgency: urgency);

            Assert.Equal(urgency, incident.Str("reportedUrgency"));
        }

        [Fact]
        public async Task UrgencyIsOptional()
        {
            var reporter = await _factory.CreateUserAsync();

            var incident = await reporter.ReportIncidentAsync();

            Assert.Equal(JsonValueKind.Null, incident.GetProperty("reportedUrgency").ValueKind);
        }

        [Theory]
        [InlineData("Extreme")]
        [InlineData("99")]
        public async Task ANonsenseUrgencyIsRefused(string urgency)
        {
            var reporter = await _factory.CreateUserAsync();

            await reporter.Client.PostAsJsonAsync("/api/incidents", new { title = "x", description = "y", urgency })
                .ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task UrgencyIsOnlyAHint_ItSetsNoPriorityAndStartsNoClock()
        {
            var reporter = await _factory.CreateUserAsync();

            var incident = await reporter.ReportIncidentAsync(urgency: "Critical");

            Assert.Equal(JsonValueKind.Null, incident.GetProperty("priority").ValueKind);
            Assert.Equal("NotStarted", incident.Str("slaState"));
            Assert.Equal(JsonValueKind.Null, incident.GetProperty("slaDueAtUtc").ValueKind);
        }

        [Fact]
        public async Task ThePriorityStillComesFromTheManager_TheUrgencyDoesNotStandInForIt()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync();
            var incident = await reporter.ReportIncidentAsync(urgency: "Critical");

            // Assigning with no priority is still refused: the manager must decide.
            await manager.Client.PutAsJsonAsync($"/api/incidents/{incident.GetGuid("id")}/assign",
                new { assigneeUserId = assignee.Id }).ExpectAsync(HttpStatusCode.BadRequest);

            var assigned = await manager.AssignAsync(incident, assignee, "Low");   // the manager disagrees with the reporter
            Assert.Equal("Low", assigned.Str("priority"));
            Assert.Equal("Critical", assigned.Str("reportedUrgency"));            // both are kept
        }

        [Fact]
        public async Task TheManagerCanSeeTheReportersUrgency()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var incident = await reporter.ReportIncidentAsync(urgency: "High");

            Assert.Equal("High", (await manager.GetIncidentAsync(incident)).Str("reportedUrgency"));
        }

        // ---- waiting for the reporter's confirmation ----

        [Fact]
        public async Task TheDashboardCountsResolvedIncidentsWaitingForTheReporter_ForThemAlone()
        {
            var s = await InProgressAsync();
            await Resolve(s.Assignee, s.Incident, "Done.").ExpectAsync(HttpStatusCode.OK);

            async Task<int> WaitingFor(TestUser who) =>
                (await (await who.Client.GetAsync("/api/dashboard/summary").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync())
                    .GetProperty("awaitingMyConfirmation").GetInt32();

            Assert.Equal(1, await WaitingFor(s.Reporter));
            Assert.Equal(0, await WaitingFor(s.Assignee));     // they did the work; it is not waiting on them
            Assert.Equal(0, await WaitingFor(s.Manager));

            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(0, await WaitingFor(s.Reporter));     // confirmed: nothing waits any more
        }
    }
}
