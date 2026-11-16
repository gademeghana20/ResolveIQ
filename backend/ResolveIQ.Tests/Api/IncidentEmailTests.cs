using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// Screens need to show who reported and who owns an incident. The API stores user ids,
    /// so it also returns each person's email.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class IncidentEmailTests
    {
        private readonly ApiFactory _factory;

        public IncidentEmailTests(ApiFactory factory) => _factory = factory;

        private static JsonElement RowFor(JsonElement list, JsonElement incident) =>
            list.GetProperty("items").EnumerateArray().Single(i => i.Str("id") == incident.Str("id"));

        [Fact]
        public async Task ANewIncident_ShowsTheReportersEmail_AndNoAssignee()
        {
            var reporter = await _factory.CreateUserAsync();

            var created = await reporter.ReportIncidentAsync();

            Assert.Equal(reporter.Email, created.Str("reportedByEmail"));
            Assert.Null(created.StrOrNull("assignedToEmail"));
        }

        [Fact]
        public async Task TheDetail_ShowsBothEmails_OnceAssigned()
        {
            var s = await Scenario.AssignedAsync(_factory);

            var detail = await s.Reporter.GetIncidentAsync(s.Incident);

            Assert.Equal(s.Reporter.Email, detail.Str("reportedByEmail"));
            Assert.Equal(s.Assignee.Email, detail.Str("assignedToEmail"));
        }

        [Fact]
        public async Task ChangingTheAssignee_ShowsTheNewEmailStraightAway()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var newAssignee = await _factory.CreateUserAsync();

            var reassigned = await s.Manager.AssignAsync(s.Incident, newAssignee);

            Assert.Equal(newAssignee.Email, reassigned.Str("assignedToEmail"));
        }

        [Fact]
        public async Task TheList_ShowsEmailsOnEveryRow()
        {
            var assigned = await Scenario.AssignedAsync(_factory);
            var unassigned = await assigned.Reporter.ReportIncidentAsync("Not yet assigned");

            var list = await assigned.Manager.ListIncidentsAsync("pageSize=100");

            var assignedRow = RowFor(list, assigned.Incident);
            Assert.Equal(assigned.Reporter.Email, assignedRow.Str("reportedByEmail"));
            Assert.Equal(assigned.Assignee.Email, assignedRow.Str("assignedToEmail"));

            var unassignedRow = RowFor(list, unassigned);
            Assert.Equal(assigned.Reporter.Email, unassignedRow.Str("reportedByEmail"));
            Assert.Null(unassignedRow.StrOrNull("assignedToEmail"));
        }

        [Fact]
        public async Task TheList_ShowsEmailsEvenWhenFilteredByTheSlaState()
        {
            // Filtering by SLA state takes a different route through the service, so check it too.
            var s = await Scenario.AssignedAsync(_factory);
            var number = s.Incident.Str("incidentNumber");

            var list = await s.Manager.ListIncidentsAsync($"search={number}&slaState=OnTrack");

            var row = Assert.Single(list.GetProperty("items").EnumerateArray());
            Assert.Equal(s.Reporter.Email, row.Str("reportedByEmail"));
            Assert.Equal(s.Assignee.Email, row.Str("assignedToEmail"));
        }

        [Fact]
        public async Task AnEmployeeWhoIsInvolved_SeesTheEmails_OfTheirOwnIncident()
        {
            var s = await Scenario.AssignedAsync(_factory);

            var detail = await s.Assignee.GetIncidentAsync(s.Incident);

            Assert.Equal(s.Reporter.Email, detail.Str("reportedByEmail"));
            Assert.Equal(s.Assignee.Email, detail.Str("assignedToEmail"));
        }

        [Fact]
        public async Task SomeoneUninvolved_StillCannotSeeTheIncident_SoNoEmailsLeakToThem()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var stranger = await _factory.CreateUserAsync(RoleNames.Employee);

            var list = await stranger.ListIncidentsAsync("pageSize=100");

            Assert.DoesNotContain(
                list.GetProperty("items").EnumerateArray(),
                row => row.Str("id") == s.Incident.Str("id"));
            Assert.DoesNotContain(s.Assignee.Email, list.GetRawText());
        }
    }
}
