using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// 25 incidents built once and shared by the read-only listing tests (building them is the slow part).
    /// Incident i (1 to 25) has the title "{token} item {i:D2}". Incidents 1 to 12 are assigned, with priorities
    /// cycling High, Low, Critical, Medium by (i mod 4): 1 Low, 2 Critical, 3 Medium, 4 High, 5 Low ...
    /// Incidents 1 to 6 have been started, and incidents 1 and 2 are resolved. Incidents 13 to 25 are untouched.
    /// </summary>
    public class ListingData
    {
        public required string Token { get; init; }
        public required TestUser Reporter { get; init; }
        public required TestUser Manager { get; init; }
        public required TestUser Assignee { get; init; }
        public required List<JsonElement> Incidents { get; init; }

        public static async Task<ListingData> BuildAsync(ApiFactory factory)
        {
            var reporter = await factory.CreateUserAsync();
            var manager = await factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await factory.CreateUserAsync();
            var token = Guid.NewGuid().ToString("N");
            string[] priorityByRemainder = { "High", "Low", "Critical", "Medium" };

            var incidents = new List<JsonElement>();
            for (var i = 1; i <= 25; i++)
            {
                var incident = await reporter.ReportIncidentAsync($"{token} item {i:D2}");

                if (i <= 12)
                {
                    incident = await manager.AssignAsync(incident, assignee, priorityByRemainder[i % 4]);
                }

                if (i <= 6)
                {
                    await assignee.SetStatusAsync(incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
                }

                if (i <= 2)
                {
                    await assignee.SetStatusAsync(incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
                }

                incidents.Add(incident);
            }

            return new ListingData { Token = token, Reporter = reporter, Manager = manager, Assignee = assignee, Incidents = incidents };
        }
    }

    [Collection(ApiCollection.Name)]
    public class ListingTests
    {
        private static Task<ListingData>? _sharedData;

        private readonly ApiFactory _factory;

        public ListingTests(ApiFactory factory) => _factory = factory;

        private Task<ListingData> DataAsync() => _sharedData ??= ListingData.BuildAsync(_factory);

        private static int Total(JsonElement page) => page.GetProperty("totalCount").GetInt32();

        private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

        private static string Numbers(JsonElement page) =>
            string.Join(",", Items(page).Select(i => i.Str("incidentNumber")));

        // ---- paging ----

        [Fact]
        public async Task ThePageSizeDefaultsTo20_AndThePageToOne()
        {
            var d = await DataAsync();

            var page = await d.Reporter.ListIncidentsAsync($"search={d.Token}");

            Assert.Equal(25, Total(page));
            Assert.Equal(20, Items(page).Count);
            Assert.Equal(1, page.GetProperty("page").GetInt32());
            Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
            Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
        }

        [Theory]
        [InlineData(1, 10, 10, 3)]
        [InlineData(2, 10, 10, 3)]
        [InlineData(3, 10, 5, 3)]
        [InlineData(4, 10, 0, 3)]       // past the end: empty, not an error
        [InlineData(1, 25, 25, 1)]
        [InlineData(1, 100, 25, 1)]
        [InlineData(5, 5, 5, 5)]
        public async Task PagingGivesTheRightSlice(int page, int pageSize, int expectedItems, int expectedPages)
        {
            var d = await DataAsync();

            var result = await d.Reporter.ListIncidentsAsync($"search={d.Token}&page={page}&pageSize={pageSize}");

            Assert.Equal(expectedItems, Items(result).Count);
            Assert.Equal(expectedPages, result.GetProperty("totalPages").GetInt32());
            Assert.Equal(25, Total(result));
        }

        [Fact]
        public async Task WalkingThroughAllThePages_ShowsEveryIncidentExactlyOnce()
        {
            var d = await DataAsync();
            var seen = new List<string>();

            for (var page = 1; page <= 5; page++)
            {
                var result = await d.Reporter.ListIncidentsAsync($"search={d.Token}&page={page}&pageSize=7");
                seen.AddRange(Items(result).Select(i => i.Str("incidentNumber")));
            }

            Assert.Equal(25, seen.Count);
            Assert.Equal(25, seen.Distinct().Count());
        }

        [Theory]
        [InlineData("page=0")]
        [InlineData("page=-1")]
        [InlineData("pageSize=0")]
        [InlineData("pageSize=-5")]
        [InlineData("pageSize=101")]
        [InlineData("page=abc")]
        [InlineData("status=Banana")]
        [InlineData("priority=Urgent")]
        [InlineData("slaState=Banana")]
        [InlineData("sortBy=Banana")]
        [InlineData("sortDirection=Sideways")]
        [InlineData("categoryId=not-a-guid")]
        public async Task BadQueryOptionsAreRejected(string query)
        {
            var d = await DataAsync();

            await d.Reporter.Client.GetAsync($"/api/incidents?{query}").ExpectAsync(HttpStatusCode.BadRequest);
        }

        // ---- search ----

        [Fact]
        public async Task SearchFindsByTitle_IgnoringCapitals_AndByPartOfAWord()
        {
            var d = await DataAsync();

            Assert.Equal(25, Total(await d.Manager.ListIncidentsAsync($"search={d.Token.ToUpperInvariant()}")));
            Assert.Equal(1, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}%20item%2007")));
            Assert.Equal(0, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}%20nothing")));
        }

        [Fact]
        public async Task SearchFindsByIncidentNumber()
        {
            var d = await DataAsync();
            var number = d.Incidents[9].Str("incidentNumber");

            var result = await d.Manager.ListIncidentsAsync($"search={number}");

            Assert.Contains(Items(result), i => i.Str("incidentNumber") == number);
        }

        [Fact]
        public async Task SearchFindsByDescription()
        {
            var reporter = await _factory.CreateUserAsync();
            var marker = $"desc-{Guid.NewGuid():N}";
            await reporter.ReportIncidentAsync("Plain title", $"The cause is {marker} again");

            Assert.Equal(1, Total(await reporter.ListIncidentsAsync($"search={marker}")));
        }

        [Theory]
        [InlineData("%")]
        [InlineData("_")]
        [InlineData("[a-z]")]
        public async Task SearchTreatsWildcardCharactersAsPlainText(string search)
        {
            var d = await DataAsync();

            // None of the titles contain these characters, so nothing should match.
            Assert.Equal(0, Total(await d.Reporter.ListIncidentsAsync($"search={Uri.EscapeDataString(search)}")));
        }

        // ---- filters ----

        [Theory]
        [InlineData("Open", 19)]
        [InlineData("InProgress", 4)]
        [InlineData("Resolved", 2)]
        [InlineData("OnHold", 0)]
        [InlineData("Closed", 0)]
        public async Task FilterByStatus(string status, int expected)
        {
            var d = await DataAsync();

            Assert.Equal(expected, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&status={status}")));
        }

        [Theory]
        [InlineData("Critical", 3)]
        [InlineData("High", 3)]
        [InlineData("Medium", 3)]
        [InlineData("Low", 3)]
        public async Task FilterByPriority(string priority, int expected)
        {
            var d = await DataAsync();

            Assert.Equal(expected, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&priority={priority}")));
        }

        [Fact]
        public async Task FilterByWhoOwnsIt()
        {
            var d = await DataAsync();

            Assert.Equal(13, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&unassigned=true")));
            Assert.Equal(12, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&assignedToUserId={d.Assignee.Id}")));
            Assert.Equal(12, Total(await d.Assignee.ListIncidentsAsync("assignedToMe=true")));
            Assert.Equal(0, Total(await d.Assignee.ListIncidentsAsync("reportedByMe=true")));
            Assert.Equal(25, Total(await d.Reporter.ListIncidentsAsync("reportedByMe=true")));
            Assert.Equal(0, Total(await d.Reporter.ListIncidentsAsync("assignedToMe=true")));
        }

        [Fact]
        public async Task FiltersCombine()
        {
            var d = await DataAsync();

            // Started (1 to 6, minus the resolved 1 and 2) and High: only incident 4.
            Assert.Equal(1, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&status=InProgress&priority=High")));
            Assert.Equal(1, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&status=InProgress&priority=Critical")));
            Assert.Equal(0, Total(await d.Manager.ListIncidentsAsync($"search={d.Token}&status=Open&unassigned=false&priority=High&assignedToUserId={Guid.NewGuid()}")));
        }

        [Fact]
        public async Task FilterByCategory()
        {
            var reporter = await _factory.CreateUserAsync();
            var categories = (await (await reporter.Client.GetAsync("/api/categories")).ReadJsonAsync()).EnumerateArray().ToList();
            var hardware = categories.First(c => c.Str("name") == "Hardware").GetGuid("id");
            var network = categories.First(c => c.Str("name") == "Network").GetGuid("id");

            await reporter.ReportIncidentAsync("one", categoryId: hardware);
            await reporter.ReportIncidentAsync("two", categoryId: hardware);
            await reporter.ReportIncidentAsync("three", categoryId: network);
            await reporter.ReportIncidentAsync("four");

            Assert.Equal(2, Total(await reporter.ListIncidentsAsync($"categoryId={hardware}")));
            Assert.Equal(1, Total(await reporter.ListIncidentsAsync($"categoryId={network}")));
            Assert.Equal(4, Total(await reporter.ListIncidentsAsync()));
        }

        [Fact]
        public async Task FiltersNeverWidenWhatAUserMaySee()
        {
            var d = await DataAsync();
            var stranger = await _factory.CreateUserAsync();

            Assert.Equal(0, Total(await stranger.ListIncidentsAsync($"search={d.Token}")));
            Assert.Equal(0, Total(await stranger.ListIncidentsAsync($"assignedToUserId={d.Assignee.Id}")));
            Assert.Equal(0, Total(await stranger.ListIncidentsAsync("unassigned=true")));
        }

        // ---- sorting ----

        [Fact]
        public async Task SortByTitle_BothWays()
        {
            var d = await DataAsync();

            var ascending = Items(await d.Manager.ListIncidentsAsync($"search={d.Token}&sortBy=title&sortDirection=asc&pageSize=25"))
                .Select(i => i.Str("title")).ToList();
            var descending = Items(await d.Manager.ListIncidentsAsync($"search={d.Token}&sortBy=title&sortDirection=desc&pageSize=25"))
                .Select(i => i.Str("title")).ToList();

            Assert.Equal(25, ascending.Count);
            Assert.Equal(ascending.OrderBy(t => t, StringComparer.Ordinal), ascending);
            Assert.Equal(ascending.AsEnumerable().Reverse(), descending);
        }

        [Fact]
        public async Task SortByPriority_PutsTheMostUrgentFirst_AndUnprioritisedLast()
        {
            var d = await DataAsync();

            var items = Items(await d.Manager.ListIncidentsAsync($"search={d.Token}&sortBy=priority&sortDirection=desc&pageSize=25"));
            var priorities = items.Select(i => i.StrOrNull("priority")).ToList();

            Assert.Equal(
                Enumerable.Repeat("Critical", 3)
                    .Concat(Enumerable.Repeat("High", 3))
                    .Concat(Enumerable.Repeat("Medium", 3))
                    .Concat(Enumerable.Repeat("Low", 3))
                    .Concat(Enumerable.Repeat<string?>(null, 13)),
                priorities);
        }

        [Fact]
        public async Task SortByCreatedDate_NewestFirstByDefault()
        {
            var d = await DataAsync();

            var newestFirst = Items(await d.Reporter.ListIncidentsAsync($"search={d.Token}&pageSize=25"))
                .Select(i => i.Str("title")).ToList();
            var oldestFirst = Items(await d.Reporter.ListIncidentsAsync($"search={d.Token}&pageSize=25&sortDirection=asc"))
                .Select(i => i.Str("title")).ToList();

            Assert.Equal($"{d.Token} item 25", newestFirst.First());
            Assert.Equal($"{d.Token} item 01", oldestFirst.First());
        }

        [Fact]
        public async Task TheOrderIsStable_SoPagesNeverOverlap()
        {
            var d = await DataAsync();

            // Many incidents share one priority, so the order depends on the tie-breaker. Two reads must agree.
            var first = Numbers(await d.Manager.ListIncidentsAsync($"search={d.Token}&sortBy=priority&pageSize=25"));
            var second = Numbers(await d.Manager.ListIncidentsAsync($"search={d.Token}&sortBy=priority&pageSize=25"));

            Assert.Equal(first, second);
        }
    }

    [Collection(ApiCollection.Name)]
    public class SlaStateFilterTests
    {
        private readonly ApiFactory _factory;

        public SlaStateFilterTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task FilteringByTheCalculatedSlaState_WorksAndPages()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync();
            var token = Guid.NewGuid().ToString("N");

            // Three untouched, one High (8 hours), one Low (48 hours), one Critical that is resolved straight away.
            for (var i = 1; i <= 3; i++)
            {
                await reporter.ReportIncidentAsync($"{token} untouched {i}");
            }

            await manager.AssignAsync(await reporter.ReportIncidentAsync($"{token} high"), assignee, "High");
            await manager.AssignAsync(await reporter.ReportIncidentAsync($"{token} low"), assignee, "Low");

            var critical = await manager.AssignAsync(await reporter.ReportIncidentAsync($"{token} critical"), assignee, "Critical");
            await assignee.SetStatusAsync(critical, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await assignee.SetStatusAsync(critical, "Resolved").ExpectAsync(HttpStatusCode.OK);

            _factory.Time.Advance(TimeSpan.FromHours(9));    // the High one is now late; the Low one is fine

            async Task<JsonElement> Page(string state, string extra = "") =>
                await manager.ListIncidentsAsync($"search={token}&slaState={state}{extra}");

            Assert.Equal(3, (await Page("NotStarted")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1, (await Page("Breached")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1, (await Page("OnTrack")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1, (await Page("Met")).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Page("AtRisk")).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Page("Paused")).GetProperty("totalCount").GetInt32());

            // Paging still works on this (in-memory) path: 3 matches, 2 per page.
            var second = await Page("NotStarted", "&pageSize=2&page=2");
            Assert.Equal(1, second.GetProperty("items").GetArrayLength());
            Assert.Equal(3, second.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, second.GetProperty("totalPages").GetInt32());
        }
    }
}
