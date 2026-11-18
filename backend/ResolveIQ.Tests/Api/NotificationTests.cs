using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Infrastructure.Data;
using ResolveIQ.Infrastructure.Services;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// People are told about what happens to incidents they are involved in: the bell. Everything is saved in the same
    /// moment as the change itself, a person is never told about their own action, and nobody sees anyone else's.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class NotificationTests
    {
        private readonly ApiFactory _factory;

        public NotificationTests(ApiFactory factory) => _factory = factory;

        private static async Task<JsonElement> BellAsync(TestUser who) =>
            await (await who.Client.GetAsync("/api/notifications").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();

        private static async Task<List<string>> MessagesAsync(TestUser who) =>
            (await BellAsync(who)).GetProperty("items").EnumerateArray().Select(n => n.Str("message")).ToList();

        private static async Task<List<string>> KindsAsync(TestUser who) =>
            (await BellAsync(who)).GetProperty("items").EnumerateArray().Select(n => n.Str("kind")).ToList();

        private sealed record Scene(TestUser Reporter, TestUser Manager, TestUser Assignee, JsonElement Incident);

        private async Task<Scene> ReportedAsync()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync();
            return new Scene(reporter, manager, assignee, await reporter.ReportIncidentAsync("Printer jams on floor 3"));
        }

        // ---- being assigned ----

        [Fact]
        public async Task AssigningTellsTheAssignee_WhoDidIt_AndTheReporter_WhoNowHasIt()
        {
            var s = await ReportedAsync();

            await s.Manager.AssignAsync(s.Incident, s.Assignee);

            var forAssignee = (await MessagesAsync(s.Assignee)).Single();
            Assert.Contains(s.Incident.Str("incidentNumber"), forAssignee);
            Assert.Contains("was assigned to you", forAssignee);
            Assert.Contains(s.Manager.Email, forAssignee);

            var forReporter = (await MessagesAsync(s.Reporter)).Single();
            Assert.Contains($"was assigned to {s.Assignee.Email}", forReporter);
        }

        [Fact]
        public async Task ThePersonWhoDidItIsNotToldAboutIt()
        {
            var s = await ReportedAsync();

            await s.Manager.AssignAsync(s.Incident, s.Assignee);

            Assert.Empty(await MessagesAsync(s.Manager));
        }

        [Fact]
        public async Task Reassigning_AlsoTellsTheOnePreviouslyAssigned()
        {
            var s = await ReportedAsync();
            var other = await _factory.CreateUserAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);

            await s.Manager.AssignAsync(assigned, other);

            Assert.Contains(await KindsAsync(s.Assignee), k => k == "Reassigned");
            Assert.Contains($"reassigned to {other.Email}", (await MessagesAsync(s.Assignee)).First());
            Assert.Contains("was assigned to you", (await MessagesAsync(other)).Single());
        }

        [Fact]
        public async Task AssigningAgainToTheSamePersonSendsNothingNew()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            var before = (await MessagesAsync(s.Assignee)).Count;

            await s.Manager.AssignAsync(assigned, s.Assignee, "High");

            Assert.Equal(before, (await MessagesAsync(s.Assignee)).Count);
        }

        [Fact]
        public async Task SomeoneWhoIsBothReporterAndAssigneeIsToldOnce_NotTwice()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var both = await _factory.CreateUserAsync();
            var incident = await both.ReportIncidentAsync();

            await manager.AssignAsync(incident, both);

            Assert.Single(await MessagesAsync(both));
        }

        // ---- status changes ----

        [Fact]
        public async Task ResolvingTellsTheReporterHowItWasFixed_AndToConfirmOrReopen()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            await s.Assignee.SetStatusAsync(assigned, "InProgress").ExpectAsync(HttpStatusCode.OK);

            await s.Assignee.SetStatusAsync(assigned, "Resolved", "Replaced the roller.").ExpectAsync(HttpStatusCode.OK);

            var bell = (await BellAsync(s.Reporter)).GetProperty("items").EnumerateArray().First();
            Assert.Equal("Resolved", bell.Str("kind"));
            Assert.Contains("Replaced the roller.", bell.Str("message"));
            Assert.Contains("confirm it is fixed, or reopen it", bell.Str("message"));
            Assert.Equal(s.Incident.GetGuid("id"), bell.GetGuid("incidentId"));
        }

        [Fact]
        public async Task ReopeningTellsTheAssignee_AndClosingTellsBoth()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            await s.Assignee.SetStatusAsync(assigned, "InProgress");
            await s.Assignee.SetStatusAsync(assigned, "Resolved");

            await s.Reporter.SetStatusAsync(assigned, "InProgress").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("Reopened", (await KindsAsync(s.Assignee)).First());
            Assert.Contains("was reopened", (await MessagesAsync(s.Assignee)).First());

            await s.Assignee.SetStatusAsync(assigned, "Resolved");
            await s.Reporter.SetStatusAsync(assigned, "Closed").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal("Closed", (await KindsAsync(s.Assignee)).First());
            Assert.DoesNotContain("Closed", await KindsAsync(s.Reporter));   // the reporter closed it themselves
        }

        [Fact]
        public async Task OrdinaryStatusMovesTellTheOtherPerson()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);

            await s.Assignee.SetStatusAsync(assigned, "InProgress").ExpectAsync(HttpStatusCode.OK);

            Assert.Contains("is now in progress", (await MessagesAsync(s.Reporter)).First());
            Assert.DoesNotContain(await MessagesAsync(s.Assignee), m => m.Contains("is now in progress")); // they did it
        }

        // ---- comments and priority ----

        [Fact]
        public async Task ACommentTellsTheReporterAndTheAssignee_ButNotItsAuthor()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);

            await s.Reporter.Client.PostAsJsonAsync($"/api/incidents/{assigned.GetGuid("id")}/comments", new { text = "Any news?" })
                .ExpectAsync(HttpStatusCode.Created);

            Assert.Contains($"{s.Reporter.Email} commented on", (await MessagesAsync(s.Assignee)).First());
            Assert.DoesNotContain(await KindsAsync(s.Reporter), k => k == "Comment");
        }

        [Fact]
        public async Task AManagersCommentReachesBothPeopleOnTheIncident()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);

            await s.Manager.Client.PostAsJsonAsync($"/api/incidents/{assigned.GetGuid("id")}/comments", new { text = "Please prioritise." })
                .ExpectAsync(HttpStatusCode.Created);

            Assert.Contains(await KindsAsync(s.Reporter), k => k == "Comment");
            Assert.Contains(await KindsAsync(s.Assignee), k => k == "Comment");
        }

        [Fact]
        public async Task AFailedCommentLeavesNoNotificationBehind()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            var before = (await MessagesAsync(s.Assignee)).Count;

            await s.Reporter.Client.PostAsJsonAsync($"/api/incidents/{assigned.GetGuid("id")}/comments", new { text = "   " })
                .ExpectAsync(HttpStatusCode.BadRequest);

            Assert.Equal(before, (await MessagesAsync(s.Assignee)).Count);
        }

        [Fact]
        public async Task ChangingThePriorityTellsTheAssignee()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee, "Low");

            await s.Manager.Client.PutAsJsonAsync($"/api/incidents/{assigned.GetGuid("id")}/priority", new { priority = "Critical" })
                .ExpectAsync(HttpStatusCode.OK);

            Assert.Contains("is now Critical", (await MessagesAsync(s.Assignee)).First());
        }

        // ---- the bell itself ----

        [Fact]
        public async Task TheBellListsNewestFirst_AndCountsUnread()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            _factory.Time.Advance(TimeSpan.FromMinutes(1));
            await s.Assignee.SetStatusAsync(assigned, "InProgress");

            var bell = await BellAsync(s.Reporter);

            Assert.Equal(2, bell.GetProperty("unreadCount").GetInt32());
            Assert.Contains("is now in progress", bell.GetProperty("items")[0].Str("message"));   // the later one first
            Assert.False(bell.GetProperty("items")[0].GetProperty("isRead").GetBoolean());
        }

        [Fact]
        public async Task MarkingOneRead_LowersTheCount_AndLeavesTheOthers()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            await s.Assignee.SetStatusAsync(assigned, "InProgress");
            var items = (await BellAsync(s.Reporter)).GetProperty("items").EnumerateArray().ToList();

            await s.Reporter.Client.PutAsync($"/api/notifications/{items[0].GetGuid("id")}/read", null).ExpectAsync(HttpStatusCode.NoContent);

            var bell = await BellAsync(s.Reporter);
            Assert.Equal(1, bell.GetProperty("unreadCount").GetInt32());
            Assert.True(bell.GetProperty("items")[0].GetProperty("isRead").GetBoolean());
            Assert.False(bell.GetProperty("items")[1].GetProperty("isRead").GetBoolean());
        }

        [Fact]
        public async Task MarkingAllRead_ClearsTheCount_ForThatPersonOnly()
        {
            var s = await ReportedAsync();
            var assigned = await s.Manager.AssignAsync(s.Incident, s.Assignee);
            await s.Assignee.SetStatusAsync(assigned, "InProgress");

            await s.Reporter.Client.PutAsync("/api/notifications/read-all", null).ExpectAsync(HttpStatusCode.NoContent);

            Assert.Equal(0, (await BellAsync(s.Reporter)).GetProperty("unreadCount").GetInt32());
            Assert.Equal(1, (await BellAsync(s.Assignee)).GetProperty("unreadCount").GetInt32());
        }

        [Fact]
        public async Task MarkingReadTwiceIsHarmless()
        {
            var s = await ReportedAsync();
            await s.Manager.AssignAsync(s.Incident, s.Assignee);
            var id = (await BellAsync(s.Assignee)).GetProperty("items")[0].GetGuid("id");

            await s.Assignee.Client.PutAsync($"/api/notifications/{id}/read", null).ExpectAsync(HttpStatusCode.NoContent);
            await s.Assignee.Client.PutAsync($"/api/notifications/{id}/read", null).ExpectAsync(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task NobodyCanReadOrMarkAnotherPersonsNotification()
        {
            var s = await ReportedAsync();
            await s.Manager.AssignAsync(s.Incident, s.Assignee);
            var theirs = (await BellAsync(s.Assignee)).GetProperty("items")[0].GetGuid("id");

            await s.Reporter.Client.PutAsync($"/api/notifications/{theirs}/read", null).ExpectAsync(HttpStatusCode.NotFound);

            Assert.DoesNotContain(
                theirs.ToString(),
                (await BellAsync(s.Reporter)).GetProperty("items").EnumerateArray().Select(n => n.Str("id")));
            Assert.Equal(1, (await BellAsync(s.Assignee)).GetProperty("unreadCount").GetInt32());   // still unread
        }

        [Fact]
        public async Task AnUnknownNotificationIsNotFound()
        {
            var person = await _factory.CreateUserAsync();

            await person.Client.PutAsync($"/api/notifications/{Guid.NewGuid()}/read", null).ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task TheBellNeedsALogin()
        {
            await _factory.CreateClient().GetAsync("/api/notifications").ExpectAsync(HttpStatusCode.Unauthorized);
            await _factory.CreateClient().PutAsync("/api/notifications/read-all", null).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task SomeoneWithNothingToReadGetsAnEmptyBell()
        {
            var person = await _factory.CreateUserAsync();

            var bell = await BellAsync(person);

            Assert.Equal(0, bell.GetProperty("unreadCount").GetInt32());
            Assert.Equal(0, bell.GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task TheBellShowsTheNewestThirty_ButCountsEveryUnreadOne()
        {
            var person = await _factory.CreateUserAsync();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                for (var i = 0; i < 35; i++)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = person.Id,
                        Kind = "Info",
                        Message = $"Message {i}",
                        CreatedAtUtc = _factory.Time.GetUtcNow().UtcDateTime.AddSeconds(i)
                    });
                }
                await db.SaveChangesAsync();
            }

            var bell = await BellAsync(person);

            Assert.Equal(35, bell.GetProperty("unreadCount").GetInt32());
            Assert.Equal(NotificationService.PageSize, bell.GetProperty("items").GetArrayLength());
            Assert.Equal("Message 34", bell.GetProperty("items")[0].Str("message"));
        }

        [Fact]
        public async Task ANotificationNeverLeaksPastTheLineOfAnIncidentOnesNotInvolvedIn()
        {
            var s = await ReportedAsync();
            var bystander = await _factory.CreateUserAsync();

            await s.Manager.AssignAsync(s.Incident, s.Assignee);

            Assert.Empty(await MessagesAsync(bystander));
        }

        // ---- tidying up ----

        [Fact]
        public async Task TheHousekeepingRemovesOldReadNotifications_AndVeryOldOnes_ButKeepsRecentUnreadOnes()
        {
            var person = await _factory.CreateUserAsync();
            var now = _factory.Time.GetUtcNow().UtcDateTime;

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Notifications.AddRange(
                    new Notification { UserId = person.Id, Kind = "Info", Message = "old and read", CreatedAtUtc = now.AddDays(-40), ReadAtUtc = now.AddDays(-35) },
                    new Notification { UserId = person.Id, Kind = "Info", Message = "very old, unread", CreatedAtUtc = now.AddDays(-100) },
                    new Notification { UserId = person.Id, Kind = "Info", Message = "recent, read", CreatedAtUtc = now.AddDays(-5), ReadAtUtc = now.AddDays(-4) },
                    new Notification { UserId = person.Id, Kind = "Info", Message = "old but unread", CreatedAtUtc = now.AddDays(-40) });
                await db.SaveChangesAsync();
            }

            using (var scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<MaintenanceJobs>().DeleteOldNotificationsAsync();
            }

            var left = await MessagesAsync(person);
            Assert.Equal(new[] { "recent, read", "old but unread" }, left);
        }
    }

    /// <summary>
    /// A resolved incident that its reporter never confirms or reopens is closed by the app after a few days,
    /// and the history says the app did it.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class AutoCloseTests
    {
        private readonly ApiFactory _factory;

        public AutoCloseTests(ApiFactory factory) => _factory = factory;

        private async Task<(TestUser Reporter, TestUser Assignee, JsonElement Incident)> ResolvedAsync()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync();
            var incident = await manager.AssignAsync(await reporter.ReportIncidentAsync(), assignee);
            await assignee.SetStatusAsync(incident, "InProgress");
            await assignee.SetStatusAsync(incident, "Resolved", "Fixed.").ExpectAsync(HttpStatusCode.OK);
            return (reporter, assignee, incident);
        }

        private async Task<int> RunAutoCloseAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MaintenanceJobs>().CloseStaleResolvedIncidentsAsync();
        }

        [Fact]
        public async Task AfterSevenDaysResolved_ItIsClosedAutomatically()
        {
            var (reporter, _, incident) = await ResolvedAsync();

            _factory.Time.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));
            Assert.True(await RunAutoCloseAsync() >= 1);

            var closed = await reporter.GetIncidentAsync(incident);
            Assert.Equal("Closed", closed.Str("status"));
            Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAtUtc").ValueKind);
            Assert.Equal("Fixed.", closed.Str("resolutionNotes"));
        }

        [Fact]
        public async Task NotBeforeThen()
        {
            var (reporter, _, incident) = await ResolvedAsync();

            _factory.Time.Advance(TimeSpan.FromDays(6));
            await RunAutoCloseAsync();

            Assert.Equal("Resolved", (await reporter.GetIncidentAsync(incident)).Str("status"));
        }

        [Fact]
        public async Task TheHistorySaysTheAppDidIt_NotAPerson()
        {
            var (reporter, _, incident) = await ResolvedAsync();
            _factory.Time.Advance(TimeSpan.FromDays(8));
            await RunAutoCloseAsync();

            var history = (await (await reporter.Client.GetAsync($"/api/incidents/{incident.GetGuid("id")}/history")
                .ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync()).EnumerateArray().ToList();

            var status = history.Last(h => h.Str("fieldChanged") == "Status");
            Assert.Equal("Closed", status.Str("newValue"));
            Assert.Equal(JsonValueKind.Null, status.GetProperty("changedBy").ValueKind);          // no person
            Assert.Contains("automatically", history.Single(h => h.Str("fieldChanged") == "AutoClosed").Str("newValue"));
        }

        [Fact]
        public async Task TheReporterAndTheAssigneeAreTold()
        {
            var (reporter, assignee, incident) = await ResolvedAsync();
            _factory.Time.Advance(TimeSpan.FromDays(8));
            await RunAutoCloseAsync();

            foreach (var person in new[] { reporter, assignee })
            {
                var bell = await (await person.Client.GetAsync("/api/notifications")).ReadJsonAsync();
                var first = bell.GetProperty("items")[0];
                Assert.Equal("AutoClosed", first.Str("kind"));
                Assert.Contains("closed automatically", first.Str("message"));
                Assert.Equal(incident.GetGuid("id"), first.GetGuid("incidentId"));
            }
        }

        [Fact]
        public async Task AReopenedIncidentIsNotClosedWhileItIsBeingWorked()
        {
            var (reporter, _, incident) = await ResolvedAsync();
            await reporter.SetStatusAsync(incident, "InProgress").ExpectAsync(HttpStatusCode.OK);   // "not fixed"

            _factory.Time.Advance(TimeSpan.FromDays(30));
            await RunAutoCloseAsync();

            Assert.Equal("InProgress", (await reporter.GetIncidentAsync(incident)).Str("status"));
        }

        [Fact]
        public async Task AnIncidentTheReporterAlreadyClosedIsLeftAlone_AndRunningAgainClosesNothingNew()
        {
            var (reporter, _, incident) = await ResolvedAsync();
            await reporter.SetStatusAsync(incident, "Closed").ExpectAsync(HttpStatusCode.OK);
            var closedAt = (await reporter.GetIncidentAsync(incident)).Time("closedAtUtc");

            _factory.Time.Advance(TimeSpan.FromDays(10));
            await RunAutoCloseAsync();
            Assert.Equal(0, await RunAutoCloseAsync());

            Assert.Equal(closedAt, (await reporter.GetIncidentAsync(incident)).Time("closedAtUtc"));
        }

        [Fact]
        public async Task ItCanBeSwitchedOff()
        {
            var factory = new AutoCloseOffApiFactory();
            await ((IAsyncLifetime)factory).InitializeAsync();
            try
            {
                var reporter = await factory.CreateUserAsync();
                var manager = await factory.CreateUserAsync(RoleNames.Manager);
                var assignee = await factory.CreateUserAsync();
                var incident = await manager.AssignAsync(await reporter.ReportIncidentAsync(), assignee);
                await assignee.SetStatusAsync(incident, "InProgress");
                await assignee.SetStatusAsync(incident, "Resolved", "Fixed.");

                factory.Time.Advance(TimeSpan.FromDays(60));
                using (var scope = factory.Services.CreateScope())
                {
                    Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<MaintenanceJobs>().CloseStaleResolvedIncidentsAsync());
                }

                var detail = await reporter.GetIncidentAsync(incident);
                Assert.Equal("Resolved", detail.Str("status"));
                Assert.Equal(JsonValueKind.Null, detail.GetProperty("autoCloseAtUtc").ValueKind);   // and it does not promise to
            }
            finally
            {
                await ((IAsyncLifetime)factory).DisposeAsync();
            }
        }
    }

    public class AutoCloseOffApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Maintenance:AutoCloseResolvedAfterDays"] = "0"
        };
    }
}
