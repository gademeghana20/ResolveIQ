using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>One reporter, one manager and one assignee, with an incident that has been assigned.</summary>
    public class Scenario
    {
        public TestUser Reporter { get; init; } = null!;
        public TestUser Manager { get; init; } = null!;
        public TestUser Assignee { get; init; } = null!;
        public JsonElement Incident { get; set; }

        public Guid Id => Incident.GetGuid("id");

        public string Url => $"/api/incidents/{Id}";

        public static async Task<Scenario> UnassignedAsync(ApiFactory factory)
        {
            var scenario = new Scenario
            {
                Reporter = await factory.CreateUserAsync(),
                Manager = await factory.CreateUserAsync(RoleNames.Manager),
                Assignee = await factory.CreateUserAsync()
            };

            scenario.Incident = await scenario.Reporter.ReportIncidentAsync();
            return scenario;
        }

        public static async Task<Scenario> AssignedAsync(ApiFactory factory, string priority = "High")
        {
            var scenario = new Scenario
            {
                Reporter = await factory.CreateUserAsync(),
                Manager = await factory.CreateUserAsync(RoleNames.Manager),
                Assignee = await factory.CreateUserAsync()
            };

            scenario.Incident = await scenario.Reporter.ReportIncidentAsync();
            scenario.Incident = await scenario.Manager.AssignAsync(scenario.Incident, scenario.Assignee, priority);
            return scenario;
        }

        public async Task<JsonElement> RefreshAsync() => Incident = await Manager.GetIncidentAsync(Incident);

        public async Task<JsonElement> HistoryAsync()
        {
            var response = await Manager.Client.GetAsync($"{Url}/history");
            await response.ExpectAsync(HttpStatusCode.OK);
            return await response.ReadJsonAsync();
        }
    }

    [Collection(ApiCollection.Name)]
    public class AssignTests
    {
        private readonly ApiFactory _factory;

        public AssignTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task OnlyManagersAndAdminsMayAssign()
        {
            var s = await Scenario.AssignedAsync(_factory);

            await s.Reporter.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = s.Assignee.Id, priority = "Low" })
                .ExpectAsync(HttpStatusCode.Forbidden);
            await s.Assignee.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = s.Assignee.Id, priority = "Low" })
                .ExpectAsync(HttpStatusCode.Forbidden);

            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            await admin.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = s.Assignee.Id })
                .ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AnIncidentWithNoPriorityCannotBeAssignedWithoutOne()
        {
            var s = await Scenario.UnassignedAsync(_factory);

            var response = await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = s.Assignee.Id })
                .ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("Priority", out _));
        }

        [Fact]
        public async Task Assigning_SetsTheOwnerAndPriority_AndStartsTheSlaClock()
        {
            var s = await Scenario.UnassignedAsync(_factory);

            var incident = await s.Manager.AssignAsync(s.Incident, s.Assignee, "High");

            Assert.Equal(s.Assignee.Id, incident.Str("assignedToUserId"));
            Assert.Equal("High", incident.Str("priority"));
            Assert.Equal("Open", incident.Str("status"));      // assigning does not start work
            Assert.Equal("OnTrack", incident.Str("slaState"));
            Assert.Equal(TimeSpan.FromHours(8), incident.Time("slaDueAtUtc") - incident.Time("slaStartedAtUtc"));
        }

        [Fact]
        public async Task Reassigning_ChangesTheOwnerButDoesNotRestartTheClock()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var originalStart = s.Incident.Time("slaStartedAtUtc");
            var secondPerson = await _factory.CreateUserAsync();

            _factory.Time.Advance(TimeSpan.FromHours(3));
            var incident = await s.Manager.AssignAsync(s.Incident, secondPerson, priority: null);

            Assert.Equal(secondPerson.Id, incident.Str("assignedToUserId"));
            Assert.Equal(originalStart, incident.Time("slaStartedAtUtc"));
        }

        [Fact]
        public async Task Assigning_RejectsBadTargets()
        {
            var s = await Scenario.UnassignedAsync(_factory);

            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = "no-such-user", priority = "High" })
                .ExpectAsync(HttpStatusCode.BadRequest);
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = "", priority = "High" })
                .ExpectAsync(HttpStatusCode.BadRequest);
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = s.Assignee.Id, priority = "Urgent" })
                .ExpectAsync(HttpStatusCode.BadRequest);
            await s.Manager.Client.PutAsJsonAsync($"/api/incidents/{Guid.NewGuid()}/assign", new { assigneeUserId = s.Assignee.Id, priority = "High" })
                .ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task AClosedIncidentCannotBeAssigned()
        {
            var s = await Scenario.AssignedAsync(_factory);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);

            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign", new { assigneeUserId = s.Assignee.Id })
                .ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Assigning_IsRecordedInTheHistory_InOrder()
        {
            var s = await Scenario.UnassignedAsync(_factory);
            var second = await _factory.CreateUserAsync();

            await s.Manager.AssignAsync(s.Incident, s.Assignee, "Medium");
            await s.Manager.AssignAsync(s.Incident, second, priority: null);

            var rows = (await s.HistoryAsync()).EnumerateArray().ToList();
            var assigned = rows.Where(r => r.Str("fieldChanged") == "AssignedTo").ToList();

            Assert.Equal(2, assigned.Count);
            Assert.Null(assigned[0].StrOrNull("oldValue"));
            Assert.Equal(s.Assignee.Email, assigned[0].Str("newValue"));
            Assert.Equal(s.Assignee.Email, assigned[1].Str("oldValue"));
            Assert.Equal(second.Email, assigned[1].Str("newValue"));
            Assert.All(assigned, r => Assert.Equal(s.Manager.Email, r.Str("changedBy")));
            Assert.Contains(rows, r => r.Str("fieldChanged") == "Priority" && r.Str("newValue") == "Medium");
            Assert.Contains(rows, r => r.Str("fieldChanged") == "SlaClock" && r.Str("newValue") == "Started");
        }
    }

    [Collection(ApiCollection.Name)]
    public class StatusWorkflowTests
    {
        private readonly ApiFactory _factory;

        public StatusWorkflowTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TheFullLifecycle_WorksForTheRightPeople()
        {
            var s = await Scenario.AssignedAsync(_factory);

            // The assignee does the work.
            var started = await (await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Equal("InProgress", started.Str("status"));

            var held = await (await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "Waiting for a part").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Equal("OnHold", held.Str("status"));

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            var resolved = await (await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.NotNull(resolved.TimeOrNull("resolvedAtUtc"));

            // The reporter confirms the fix.
            var closed = await (await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Equal("Closed", closed.Str("status"));
        }

        [Fact]
        public async Task WorkCannotStartOnAnUnassignedIncident_NotEvenByAManager()
        {
            var s = await Scenario.UnassignedAsync(_factory);

            await s.Manager.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task OnlyTheAssigneeDoesTheWork_AndOnlyTheReporterClosesIt()
        {
            var s = await Scenario.AssignedAsync(_factory);

            await s.Reporter.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.Forbidden);

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);

            await s.Assignee.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.Forbidden);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.Forbidden);  // reopening is the reporter's call
            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ManagersMayMoveAnyIncidentAlongTheValidPath()
        {
            var s = await Scenario.AssignedAsync(_factory);

            await s.Manager.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Manager.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            await s.Manager.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task SkippingStepsAndUnknownStatusesAreRejected()
        {
            var s = await Scenario.AssignedAsync(_factory);

            await s.Manager.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.BadRequest);     // Open -> Closed
            await s.Manager.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.BadRequest);   // Open -> Resolved
            await s.Manager.SetStatusAsync(s.Incident, "Open").ExpectAsync(HttpStatusCode.BadRequest);       // same status
            await s.Manager.SetStatusAsync(s.Incident, "Banana").ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task PuttingAnIncidentOnHold_NeedsARealReason_WhichIsRecorded()
        {
            var s = await Scenario.AssignedAsync(_factory);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            await s.Assignee.SetStatusAsync(s.Incident, "OnHold").ExpectAsync(HttpStatusCode.BadRequest);
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "   ").ExpectAsync(HttpStatusCode.BadRequest);
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", new string('x', 501)).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Assignee.SetStatusAsync(s.Incident, "OnHold", "Waiting for vendor").ExpectAsync(HttpStatusCode.OK);

            var rows = (await s.HistoryAsync()).EnumerateArray().ToList();
            Assert.Contains(rows, r => r.Str("fieldChanged") == "OnHoldReason" && r.Str("newValue") == "Waiting for vendor");
        }

        [Fact]
        public async Task ReopeningClearsTheResolvedTime_AndALaterResolveSetsItAgain()
        {
            var s = await Scenario.AssignedAsync(_factory);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);

            var reopened = await (await s.Reporter.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Null(reopened.TimeOrNull("resolvedAtUtc"));

            var resolvedAgain = await (await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.NotNull(resolvedAgain.TimeOrNull("resolvedAtUtc"));
        }

        [Fact]
        public async Task AClosedIncidentIsFinal()
        {
            var s = await Scenario.AssignedAsync(_factory);
            await s.Manager.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Manager.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            await s.Manager.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);

            foreach (var status in new[] { "Open", "InProgress", "OnHold", "Resolved" })
            {
                await s.Manager.SetStatusAsync(s.Incident, status, "reason").ExpectAsync(HttpStatusCode.BadRequest);
            }

            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Low" }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PutAsJsonAsync(s.Url, new { title = "late edit", description = "x" }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PostAsJsonAsync($"{s.Url}/comments", new { text = "too late" }).ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task EveryStatusChangeIsInTheHistory_WithWhoMadeIt()
        {
            var s = await Scenario.AssignedAsync(_factory);
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);

            var changes = (await s.HistoryAsync()).EnumerateArray()
                .Where(r => r.Str("fieldChanged") == "Status")
                .Select(r => (r.Str("oldValue"), r.Str("newValue"), r.Str("changedBy")))
                .ToList();

            Assert.Equal(new[]
            {
                ("Open", "InProgress", s.Assignee.Email),
                ("InProgress", "Resolved", s.Assignee.Email),
                ("Resolved", "Closed", s.Reporter.Email)
            }, changes);
        }
    }

    [Collection(ApiCollection.Name)]
    public class PriorityAndEditTests
    {
        private readonly ApiFactory _factory;

        public PriorityAndEditTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task OnlyManagersAndAdminsChangePriority()
        {
            var s = await Scenario.AssignedAsync(_factory, "Medium");

            await s.Reporter.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Critical" }).ExpectAsync(HttpStatusCode.Forbidden);
            await s.Assignee.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Critical" }).ExpectAsync(HttpStatusCode.Forbidden);

            var response = await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Critical" }).ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("Critical", (await response.ReadJsonAsync()).Str("priority"));
        }

        [Fact]
        public async Task ChangingPriority_RejectsNonsense_AndSameValueChangesNothing()
        {
            var s = await Scenario.AssignedAsync(_factory, "Medium");

            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Urgent" }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = 99 }).ExpectAsync(HttpStatusCode.BadRequest);

            var before = (await s.HistoryAsync()).GetArrayLength();
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Medium" }).ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(before, (await s.HistoryAsync()).GetArrayLength());
        }

        [Fact]
        public async Task PriorityIsLockedOnceResolved_AndUnlockedAgainByAReopen()
        {
            var s = await Scenario.AssignedAsync(_factory, "Medium");
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);

            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Low" }).ExpectAsync(HttpStatusCode.BadRequest);

            await s.Reporter.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);   // reopen
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/priority", new { priority = "Low" }).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task TheReporterCanEditTheText_AndEveryChangeIsRecorded()
        {
            var s = await Scenario.AssignedAsync(_factory);

            var response = await s.Reporter.Client.PutAsJsonAsync(s.Url,
                new { title = "  New title  ", description = "New description" }).ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("New title", (await response.ReadJsonAsync()).Str("title"));

            var rows = (await s.HistoryAsync()).EnumerateArray().ToList();
            Assert.Contains(rows, r => r.Str("fieldChanged") == "Title" && r.Str("newValue") == "New title" && r.Str("changedBy") == s.Reporter.Email);
            Assert.Contains(rows, r => r.Str("fieldChanged") == "Description" && r.Str("newValue") == "New description");
        }

        [Fact]
        public async Task SavingTheSameTextAgainChangesNothing()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var before = (await s.HistoryAsync()).GetArrayLength();

            await s.Reporter.Client.PutAsJsonAsync(s.Url,
                new { title = s.Incident.Str("title"), description = s.Incident.Str("description") }).ExpectAsync(HttpStatusCode.OK);

            Assert.Equal(before, (await s.HistoryAsync()).GetArrayLength());
        }

        [Fact]
        public async Task OnlyTheReporterOrAManagerMayEdit()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var body = new { title = "Edited", description = "Edited" };

            await s.Assignee.Client.PutAsJsonAsync(s.Url, body).ExpectAsync(HttpStatusCode.Forbidden);
            await s.Manager.Client.PutAsJsonAsync(s.Url, body).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task EditingRejectsEmptyOrOversizedText()
        {
            var s = await Scenario.AssignedAsync(_factory);

            await s.Reporter.Client.PutAsJsonAsync(s.Url, new { title = "", description = "d" }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PutAsJsonAsync(s.Url, new { title = "t", description = "" }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PutAsJsonAsync(s.Url, new { title = new string('x', 201), description = "d" }).ExpectAsync(HttpStatusCode.BadRequest);
        }
    }

    [Collection(ApiCollection.Name)]
    public class CommentTests
    {
        private readonly ApiFactory _factory;

        public CommentTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task ReporterManagerAndAssigneeCanAllComment_AndSeeEachOthersComments()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var url = $"{s.Url}/comments";

            var first = await (await s.Reporter.Client.PostAsJsonAsync(url, new { text = "  Still jammed.  " }).ExpectAsync(HttpStatusCode.Created)).ReadJsonAsync();
            await s.Manager.Client.PostAsJsonAsync(url, new { text = "Looking into it." }).ExpectAsync(HttpStatusCode.Created);
            await s.Assignee.Client.PostAsJsonAsync(url, new { text = "On it." }).ExpectAsync(HttpStatusCode.Created);

            Assert.Equal("Still jammed.", first.Str("text"));
            Assert.Equal(s.Reporter.Email, first.Str("authorEmail"));

            var comments = (await (await s.Assignee.Client.GetAsync(url)).ReadJsonAsync()).EnumerateArray().ToList();
            Assert.Equal(new[] { s.Reporter.Email, s.Manager.Email, s.Assignee.Email }, comments.Select(c => c.Str("authorEmail")));
        }

        [Fact]
        public async Task CommentsComeBackOldestFirst_WithUtcTimes()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var url = $"{s.Url}/comments";

            for (var i = 1; i <= 3; i++)
            {
                _factory.Time.Advance(TimeSpan.FromMinutes(5));
                await s.Reporter.Client.PostAsJsonAsync(url, new { text = $"Comment {i}" }).ExpectAsync(HttpStatusCode.Created);
            }

            var comments = (await (await s.Reporter.Client.GetAsync(url)).ReadJsonAsync()).EnumerateArray().ToList();

            Assert.Equal(new[] { "Comment 1", "Comment 2", "Comment 3" }, comments.Select(c => c.Str("text")));
            Assert.Equal(comments.Select(c => c.Time("createdAtUtc")).OrderBy(t => t), comments.Select(c => c.Time("createdAtUtc")));
            Assert.All(comments, c => Assert.EndsWith("Z", c.Str("createdAtUtc")));
        }

        [Fact]
        public async Task CommentsRejectEmptyAndOversizedText_ButAcceptExactlyTheLimit()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var url = $"{s.Url}/comments";

            await s.Reporter.Client.PostAsJsonAsync(url, new { text = "" }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PostAsJsonAsync(url, new { text = "     " }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PostAsJsonAsync(url, new { }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PostAsJsonAsync(url, new { text = new string('x', 2001) }).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.Client.PostAsJsonAsync(url, new { text = new string('x', 2000) }).ExpectAsync(HttpStatusCode.Created);
        }

        [Fact]
        public async Task CommentsAreStoredAsPlainText_AndDoNotAppearInTheHistory()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var before = (await s.HistoryAsync()).GetArrayLength();

            var response = await s.Reporter.Client.PostAsJsonAsync($"{s.Url}/comments",
                new { text = "<script>alert(1)</script> & \"quotes\"" }).ExpectAsync(HttpStatusCode.Created);

            Assert.Equal("<script>alert(1)</script> & \"quotes\"", (await response.ReadJsonAsync()).Str("text"));
            Assert.Equal(before, (await s.HistoryAsync()).GetArrayLength());
        }
    }
}
