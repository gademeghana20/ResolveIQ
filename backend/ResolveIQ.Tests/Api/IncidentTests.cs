using System.Net;
using System.Net.Http.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class IncidentCreationTests
    {
        private readonly ApiFactory _factory;

        public IncidentCreationTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task Create_MakesAnOpenUnassignedIncidentWithNoPriority_AndIgnoresWhatTheClientMustNotChoose()
        {
            var reporter = await _factory.CreateUserAsync();
            var someoneElse = await _factory.CreateUserAsync();

            var response = await reporter.Client.PostAsJsonAsync("/api/incidents", new
            {
                title = "  Laptop will not boot  ",
                description = "Black screen",
                priority = "Critical",                // not the reporter's decision
                status = "Closed",                    // always starts Open
                reportedByUserId = someoneElse.Id,    // always the logged-in user
                assignedToUserId = someoneElse.Id     // assigned later, by a manager
            }).ExpectAsync(HttpStatusCode.Created);

            var incident = await response.ReadJsonAsync();

            Assert.Matches(@"^INC-\d{6}$", incident.Str("incidentNumber"));
            Assert.Equal("Laptop will not boot", incident.Str("title"));
            Assert.Equal("Open", incident.Str("status"));
            Assert.Null(incident.StrOrNull("priority"));
            Assert.Equal(reporter.Id, incident.Str("reportedByUserId"));
            Assert.Null(incident.StrOrNull("assignedToUserId"));
            Assert.Equal("NotStarted", incident.Str("slaState"));
            Assert.Null(incident.StrOrNull("slaDueAtUtc"));
            Assert.NotNull(response.Headers.Location);
        }

        [Fact]
        public async Task Create_GivesEachIncidentItsOwnIncreasingNumber()
        {
            var reporter = await _factory.CreateUserAsync();

            var numbers = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                var incident = await reporter.ReportIncidentAsync($"Incident {i}");
                numbers.Add(int.Parse(incident.Str("incidentNumber")[4..]));
            }

            Assert.Equal(numbers.OrderBy(n => n), numbers);
            Assert.Equal(3, numbers.Distinct().Count());
        }

        [Fact]
        public async Task Create_RejectsInvalidInput()
        {
            var reporter = await _factory.CreateUserAsync();

            var badBodies = new object[]
            {
                new { title = "", description = "d" },
                new { title = "   ", description = "d" },
                new { title = new string('x', 201), description = "d" },
                new { title = "t", description = "" },
                new { title = "t", description = new string('x', 4001) },
                new { description = "d" },
                new { title = "t", description = "d", categoryId = Guid.NewGuid() }   // no such category
            };

            foreach (var body in badBodies)
            {
                await reporter.Client.PostAsJsonAsync("/api/incidents", body).ExpectAsync(HttpStatusCode.BadRequest);
            }
        }

        [Fact]
        public async Task Create_AcceptsTheLongestAllowedTextAndAValidCategory()
        {
            var reporter = await _factory.CreateUserAsync();
            var categories = await (await reporter.Client.GetAsync("/api/categories")).ReadJsonAsync();
            var hardware = categories.EnumerateArray().First(c => c.Str("name") == "Hardware");

            var incident = await reporter.ReportIncidentAsync(
                title: new string('t', 200),
                description: new string('d', 4000),
                categoryId: hardware.GetGuid("id"));

            Assert.Equal("Hardware", incident.Str("categoryName"));
            Assert.Equal(200, incident.Str("title").Length);
            Assert.Equal(4000, incident.Str("description").Length);
        }
    }

    [Collection(ApiCollection.Name)]
    public class IncidentVisibilityTests
    {
        private readonly ApiFactory _factory;

        public IncidentVisibilityTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task OnlyPeopleInvolvedCanSeeAnIncident_EveryoneElseGetsNotFound()
        {
            var reporter = await _factory.CreateUserAsync();
            var stranger = await _factory.CreateUserAsync();
            var assignee = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            var incident = await reporter.ReportIncidentAsync();
            var url = $"/api/incidents/{incident.GetGuid("id")}";

            // Before assignment: the reporter and managers/admins only.
            await reporter.Client.GetAsync(url).ExpectAsync(HttpStatusCode.OK);
            await manager.Client.GetAsync(url).ExpectAsync(HttpStatusCode.OK);
            await admin.Client.GetAsync(url).ExpectAsync(HttpStatusCode.OK);
            await stranger.Client.GetAsync(url).ExpectAsync(HttpStatusCode.NotFound);
            await assignee.Client.GetAsync(url).ExpectAsync(HttpStatusCode.NotFound);

            // After assignment the assignee can see it too; the stranger still cannot.
            await manager.AssignAsync(incident, assignee);
            await assignee.Client.GetAsync(url).ExpectAsync(HttpStatusCode.OK);
            await stranger.Client.GetAsync(url).ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task EverythingAboutAHiddenIncident_LooksLikeItDoesNotExist()
        {
            var reporter = await _factory.CreateUserAsync();
            var stranger = await _factory.CreateUserAsync();
            var incident = await reporter.ReportIncidentAsync();
            var url = $"/api/incidents/{incident.GetGuid("id")}";

            await stranger.Client.GetAsync($"{url}/history").ExpectAsync(HttpStatusCode.NotFound);
            await stranger.Client.GetAsync($"{url}/comments").ExpectAsync(HttpStatusCode.NotFound);
            await stranger.Client.PostAsJsonAsync($"{url}/comments", new { text = "hello" }).ExpectAsync(HttpStatusCode.NotFound);
            await stranger.Client.PutAsJsonAsync($"{url}/status", new { status = "InProgress" }).ExpectAsync(HttpStatusCode.NotFound);
            await stranger.Client.PutAsJsonAsync(url, new { title = "hijack", description = "x" }).ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task AnUnknownIncidentIsNotFound()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            await manager.Client.GetAsync($"/api/incidents/{Guid.NewGuid()}").ExpectAsync(HttpStatusCode.NotFound);
            await manager.Client.GetAsync($"/api/incidents/{Guid.NewGuid()}/history").ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task TheList_OnlyContainsWhatTheUserMaySee()
        {
            var reporter = await _factory.CreateUserAsync();
            var stranger = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var marker = $"vis-{Guid.NewGuid():N}";

            await reporter.ReportIncidentAsync($"{marker} one");
            await reporter.ReportIncidentAsync($"{marker} two");

            Assert.Equal(2, (await reporter.ListIncidentsAsync($"search={marker}")).GetProperty("totalCount").GetInt32());
            Assert.Equal(2, (await manager.ListIncidentsAsync($"search={marker}")).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await stranger.ListIncidentsAsync($"search={marker}")).GetProperty("totalCount").GetInt32());
        }
    }
}
