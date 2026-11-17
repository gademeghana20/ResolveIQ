using System.Net;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// The API tells each screen what the current user may do, so the screen offers only
    /// actions the API will accept. These check that the answers really reach the response.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class IncidentActionsApiTests
    {
        private readonly ApiFactory _factory;

        public IncidentActionsApiTests(ApiFactory factory) => _factory = factory;

        private static JsonElement Actions(JsonElement incident) => incident.GetProperty("actions");

        private static string[] Allowed(JsonElement incident) =>
            Actions(incident).GetProperty("allowedStatusChanges").EnumerateArray().Select(s => s.GetString()!).Order().ToArray();

        [Fact]
        public async Task ANewIncident_OffersItsReporterNothingExceptEditingIt()
        {
            var reporter = await _factory.CreateUserAsync();

            var created = await reporter.ReportIncidentAsync();

            Assert.True(Actions(created).GetProperty("canEdit").GetBoolean());
            Assert.False(Actions(created).GetProperty("canAssign").GetBoolean());
            Assert.False(Actions(created).GetProperty("canChangePriority").GetBoolean());
            Assert.Empty(Allowed(created));
        }

        [Fact]
        public async Task EachPersonSeesOnlyTheirOwnActions_OnTheSameIncident()
        {
            var s = await Scenario.AssignedAsync(_factory);

            var asManager = await s.Manager.GetIncidentAsync(s.Incident);
            Assert.True(Actions(asManager).GetProperty("canAssign").GetBoolean());
            Assert.True(Actions(asManager).GetProperty("canChangePriority").GetBoolean());
            Assert.Equal(new[] { "InProgress" }, Allowed(asManager));

            var asAssignee = await s.Assignee.GetIncidentAsync(s.Incident);
            Assert.False(Actions(asAssignee).GetProperty("canAssign").GetBoolean());
            Assert.Equal(new[] { "InProgress" }, Allowed(asAssignee));

            var asReporter = await s.Reporter.GetIncidentAsync(s.Incident);
            Assert.False(Actions(asReporter).GetProperty("canAssign").GetBoolean());
            Assert.Empty(Allowed(asReporter));
        }

        [Fact]
        public async Task TheAnswerChanges_AsTheIncidentMovesThroughItsLife()
        {
            var s = await Scenario.AssignedAsync(_factory);

            var started = await (await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Equal(new[] { "OnHold", "Resolved" }, Allowed(started));

            var resolved = await (await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Empty(Allowed(resolved));                                             // the assignee is done
            Assert.False(Actions(resolved).GetProperty("canChangePriority").GetBoolean()); // and priority is now locked

            var asReporter = await s.Reporter.GetIncidentAsync(s.Incident);
            Assert.Equal(new[] { "Closed", "InProgress" }, Allowed(asReporter));          // the reporter decides
        }

        [Fact]
        public async Task OnlyTheReporterIsOfferedEditing_AndNotOnceTheIncidentIsClosed()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            static bool CanEdit(JsonElement incident) => Actions(incident).GetProperty("canEdit").GetBoolean();

            Assert.True(CanEdit(await s.Reporter.GetIncidentAsync(s.Incident)));
            Assert.False(CanEdit(await s.Manager.GetIncidentAsync(s.Incident)));
            Assert.False(CanEdit(await s.Assignee.GetIncidentAsync(s.Incident)));
            Assert.False(CanEdit(await admin.GetIncidentAsync(s.Incident)));

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            Assert.True(CanEdit(await s.Reporter.GetIncidentAsync(s.Incident)));   // still editable while resolved

            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);
            Assert.False(CanEdit(await s.Reporter.GetIncidentAsync(s.Incident)));  // final
        }

        [Fact]
        public async Task EveryActionOffered_IsAccepted_AndAnActionNotOffered_IsRefused()
        {
            var s = await Scenario.AssignedAsync(_factory);

            // Not offered to the reporter on an Open incident: the API must refuse it too.
            var refused = await s.Reporter.SetStatusAsync(s.Incident, "InProgress");
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);

            // Offered to the assignee: the API must accept it.
            var offered = Allowed(await s.Assignee.GetIncidentAsync(s.Incident)).Single();
            await s.Assignee.SetStatusAsync(s.Incident, offered).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AManagerStartingSomeoneElsesWork_IsToldTheyAreActingOnTheirBehalf()
        {
            var s = await Scenario.AssignedAsync(_factory);

            var asManager = await s.Manager.GetIncidentAsync(s.Incident);
            var onBehalf = Assert.Single(Actions(asManager).GetProperty("onBehalfOf").EnumerateArray());
            Assert.Equal("InProgress", onBehalf.Str("status"));
            Assert.Equal("Assignee", onBehalf.Str("role"));
            Assert.Equal(s.Assignee.Email, onBehalf.Str("email"));

            // The assignee doing their own job is not acting for anyone.
            var asAssignee = await s.Assignee.GetIncidentAsync(s.Incident);
            Assert.Empty(Actions(asAssignee).GetProperty("onBehalfOf").EnumerateArray());
        }

        [Fact]
        public async Task TheHistory_RecordsTheManager_WhenTheyActForTheAssignee()
        {
            // "On behalf" does not hide who really made the change.
            var s = await Scenario.AssignedAsync(_factory);

            await s.Manager.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);

            var history = await s.HistoryAsync();
            var change = history.EnumerateArray().Single(h => h.Str("fieldChanged") == "Status");
            Assert.Equal(s.Manager.Email, change.Str("changedBy"));
        }

        [Fact]
        public async Task TheResponseToAnAssignment_AlreadyCarriesTheManagersActions()
        {
            var s = await Scenario.UnassignedAsync(_factory);

            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);

            Assert.True(Actions(assigned).GetProperty("canChangePriority").GetBoolean());
            Assert.Equal(new[] { "InProgress" }, Allowed(assigned));
        }
    }
}
