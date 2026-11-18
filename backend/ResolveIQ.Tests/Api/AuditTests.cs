using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class AuditTrailTests
    {
        private readonly ApiFactory _factory;

        public AuditTrailTests(ApiFactory factory) => _factory = factory;

        private static async Task<List<JsonElement>> LinesAsync(TestUser admin, string search, string? action = null)
        {
            var url = $"/api/audit?search={Uri.EscapeDataString(search)}" + (action is null ? "" : $"&action={action}");
            var response = await admin.Client.GetAsync(url);
            await response.ExpectAsync(HttpStatusCode.OK);
            return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
        }

        private async Task<(TestUser Admin, TestUser Person)> AdminAndPersonAsync(string role = RoleNames.Employee) =>
            (await _factory.CreateUserAsync(RoleNames.Admin), await _factory.CreateUserAsync(role));

        [Fact]
        public async Task CreatingAUser_IsRecordedWithWhoDidIt()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var email = $"newbie.{Guid.NewGuid():N}@example.com";

            await admin.Client.PostAsJsonAsync("/api/users", new { email, role = "Manager" }).ExpectAsync(HttpStatusCode.Created);

            var line = Assert.Single(await LinesAsync(admin, email));
            Assert.Equal("user.created", line.Str("action"));
            Assert.Equal(admin.Email, line.Str("actorEmail"));
            Assert.Equal(email, line.Str("targetLabel"));
            Assert.Equal("Role: Manager", line.Str("details"));
        }

        [Fact]
        public async Task ARoleChange_IsRecordedWithOldAndNew_AndAnUnchangedRoleIsNot()
        {
            var (admin, person) = await AdminAndPersonAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Manager" }).ExpectAsync(HttpStatusCode.OK);
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Manager" }).ExpectAsync(HttpStatusCode.OK);

            var line = Assert.Single(await LinesAsync(admin, person.Email, "user.role_changed"));
            Assert.Equal("Role: Employee → Manager", line.Str("details"));
            Assert.Equal(admin.Email, line.Str("actorEmail"));
        }

        [Fact]
        public async Task DeactivatingAndReactivating_AreRecorded_OnceEach()
        {
            var (admin, person) = await AdminAndPersonAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK); // already off
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = true }).ExpectAsync(HttpStatusCode.OK);

            var actions = (await LinesAsync(admin, person.Email)).Select(l => l.Str("action")).ToList();
            Assert.Equal(new[] { "user.reactivated", "user.deactivated" }, actions); // newest first
        }

        [Fact]
        public async Task ASentResetEmail_IsRecorded()
        {
            var (admin, person) = await AdminAndPersonAsync();

            await admin.Client.PostAsync($"/api/users/{person.Id}/password-reset-email", null).ExpectAsync(HttpStatusCode.OK);

            Assert.Equal("user.reset_email_sent", Assert.Single(await LinesAsync(admin, person.Email)).Str("action"));
        }

        [Fact]
        public async Task SelfRegistration_IsRecorded_WithThePersonAsTheActor()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var email = $"selfmade.{Guid.NewGuid():N}@example.com";

            await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.StandardPassword })
                .ExpectAsync(HttpStatusCode.OK);

            var line = Assert.Single(await LinesAsync(admin, email));
            Assert.Equal("user.registered", line.Str("action"));
            Assert.Equal(email, line.Str("actorEmail"));
        }

        [Fact]
        public async Task CategoryChanges_AreRecorded_ButASaveWithNothingChangedIsNot()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var name = "Audit " + Guid.NewGuid().ToString("N")[..8];

            var created = await (await admin.Client.PostAsJsonAsync("/api/categories", new { name, description = "first" })
                .ExpectAsync(HttpStatusCode.Created)).ReadJsonAsync();
            var id = created.GetGuid("id");

            var renamed = name + " renamed";
            await admin.Client.PutAsJsonAsync($"/api/categories/{id}", new { name = renamed, description = "first", isActive = true }).ExpectAsync(HttpStatusCode.OK);
            await admin.Client.PutAsJsonAsync($"/api/categories/{id}", new { name = renamed, description = "first", isActive = true }).ExpectAsync(HttpStatusCode.OK);
            await admin.Client.PutAsJsonAsync($"/api/categories/{id}", new { name = renamed, description = "first", isActive = false }).ExpectAsync(HttpStatusCode.OK);

            var lines = await LinesAsync(admin, name);
            Assert.Equal(new[] { "category.updated", "category.updated", "category.created" }, lines.Select(l => l.Str("action")).ToArray());
            Assert.Equal("Hidden", lines[0].Str("details"));
            Assert.Equal($"Name: {name} → {renamed}", lines[1].Str("details"));
        }

        [Fact]
        public async Task AnSlaChange_IsRecordedWithOldAndNewHours()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var before = await (await admin.Client.GetAsync("/api/sla/policies")).ReadJsonAsync();
            var low = before.EnumerateArray().Single(p => p.Str("priority") == "Low");
            var oldHours = low.GetProperty("resolutionTargetHours").GetInt32();
            var newHours = oldHours == 47 ? 46 : 47;

            try
            {
                await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", new { resolutionTargetHours = newHours, isActive = true }).ExpectAsync(HttpStatusCode.OK);

                var line = (await LinesAsync(admin, "Low priority", "sla.updated")).First();
                Assert.Equal($"Target: {oldHours} h → {newHours} h", line.Str("details"));
                Assert.Equal(admin.Email, line.Str("actorEmail"));
            }
            finally
            {
                await admin.Client.PutAsJsonAsync("/api/sla/policies/Low", new { resolutionTargetHours = oldHours, isActive = true });
            }
        }

        [Fact]
        public async Task CanBeFilteredByAction_AndAnUnknownActionIsRefused()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await admin.Client.GetAsync("/api/audit?action=user.created").ExpectAsync(HttpStatusCode.OK);
            await admin.Client.GetAsync("/api/audit?action=drop.table").ExpectAsync(HttpStatusCode.BadRequest);

            var lines = await LinesAsync(admin, "", "user.created");
            Assert.All(lines, l => Assert.Equal("user.created", l.Str("action")));
        }

        [Fact]
        public async Task ListsNewestFirst_InPages()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            foreach (var role in new[] { "Manager", "Employee", "Manager" })
            {
                await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role }).ExpectAsync(HttpStatusCode.OK);
            }

            var page = await (await admin.Client.GetAsync($"/api/audit?search={Uri.EscapeDataString(person.Email)}&pageSize=2")).ReadJsonAsync();

            Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, page.GetProperty("items").GetArrayLength());
            Assert.Equal("Role: Employee → Manager", page.GetProperty("items")[0].Str("details")); // the last change first
        }
    }

    [Collection(ApiCollection.Name)]
    public class DeactivateWithIncidentsTests
    {
        private readonly ApiFactory _factory;

        public DeactivateWithIncidentsTests(ApiFactory factory) => _factory = factory;

        private static async Task<JsonElement> UserAsync(TestUser admin, string id) =>
            await (await admin.Client.GetAsync($"/api/users/{id}")).ReadJsonAsync();

        private static Task<HttpResponseMessage> DeactivateAsync(TestUser admin, string id, string? reassignTo = null) =>
            admin.Client.PutAsJsonAsync($"/api/users/{id}/active", new { isActive = false, reassignToUserId = reassignTo });

        [Fact]
        public async Task TheUserListSaysHowManyUnfinishedIncidentsEachPersonHas()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            Assert.Equal(1, (await UserAsync(admin, s.Assignee.Id)).GetProperty("activeAssignedCount").GetInt32());

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);   // waiting for the reporter: not counted

            Assert.Equal(0, (await UserAsync(admin, s.Assignee.Id)).GetProperty("activeAssignedCount").GetInt32());
        }

        [Fact]
        public async Task WithAReassignment_TheIncidentsMoveToTheChosenPerson_WithHistoryAndANotification()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var heir = await _factory.CreateUserAsync();

            var response = await DeactivateAsync(admin, s.Assignee.Id, heir.Id).ExpectAsync(HttpStatusCode.OK);

            Assert.False((await response.ReadJsonAsync()).GetProperty("isActive").GetBoolean());
            var incident = await s.RefreshAsync();
            Assert.Equal(heir.Id, incident.Str("assignedToUserId"));

            var history = await (await s.Manager.Client.GetAsync($"/api/incidents/{s.Id}/history")).ReadJsonAsync();
            var move = history.EnumerateArray().Last(h => h.Str("fieldChanged") == "AssignedTo");
            Assert.Equal(s.Assignee.Email, move.Str("oldValue"));
            Assert.Equal(heir.Email, move.Str("newValue"));
            Assert.Equal(admin.Email, move.Str("changedBy"));

            var bell = await (await heir.Client.GetAsync("/api/notifications")).ReadJsonAsync();
            Assert.Contains(bell.GetProperty("items").EnumerateArray(), n => n.Str("message").Contains("was assigned to you") && n.Str("message").Contains(s.Assignee.Email));

            var audit = await (await admin.Client.GetAsync($"/api/audit?search={Uri.EscapeDataString(s.Assignee.Email)}")).ReadJsonAsync();
            Assert.Contains("Handed 1 unfinished incident(s)", audit.GetProperty("items")[0].Str("details"));
        }

        [Fact]
        public async Task WithoutAReassignment_TheIncidentsStayWhereTheyAre()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await DeactivateAsync(admin, s.Assignee.Id).ExpectAsync(HttpStatusCode.OK);

            Assert.Equal(s.Assignee.Id, (await s.RefreshAsync()).Str("assignedToUserId"));
        }

        [Fact]
        public async Task AResolvedIncidentIsNotMoved()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var heir = await _factory.CreateUserAsync();
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);

            await DeactivateAsync(admin, s.Assignee.Id, heir.Id).ExpectAsync(HttpStatusCode.OK);

            Assert.Equal(s.Assignee.Id, (await s.RefreshAsync()).Str("assignedToUserId"));
        }

        [Fact]
        public async Task ABadReassignmentTarget_IsRefused_AndNothingChanges()
        {
            var s = await Scenario.AssignedAsync(_factory);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var gone = await _factory.CreateUserAsync();
            await admin.Client.PutAsJsonAsync($"/api/users/{gone.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);

            await DeactivateAsync(admin, s.Assignee.Id, gone.Id).ExpectAsync(HttpStatusCode.BadRequest);      // inactive
            await DeactivateAsync(admin, s.Assignee.Id, s.Assignee.Id).ExpectAsync(HttpStatusCode.BadRequest); // themselves
            await DeactivateAsync(admin, s.Assignee.Id, "no-such-user").ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await UserAsync(admin, s.Assignee.Id)).GetProperty("isActive").GetBoolean());
            Assert.Equal(s.Assignee.Id, (await s.RefreshAsync()).Str("assignedToUserId"));
        }

        [Fact]
        public async Task AReassignmentMakesNoSenseWhenReactivating()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            var other = await _factory.CreateUserAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = true, reassignToUserId = other.Id })
                .ExpectAsync(HttpStatusCode.BadRequest);
        }
    }
}
