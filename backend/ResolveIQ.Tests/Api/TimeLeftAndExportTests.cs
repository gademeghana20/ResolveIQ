using System.Net;
using System.Text;
using System.Text.Json;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class TimeLeftTests
    {
        private readonly ApiFactory _factory;

        public TimeLeftTests(ApiFactory factory) => _factory = factory;

        private void Hours(double hours) => _factory.Time.Advance(TimeSpan.FromHours(hours));

        private static int? Minutes(JsonElement incident) =>
            incident.GetProperty("slaMinutesLeft").ValueKind == JsonValueKind.Null
                ? null
                : incident.GetProperty("slaMinutesLeft").GetInt32();

        private static async Task<JsonElement> InListAsync(Scenario s)
        {
            var list = await s.Manager.ListIncidentsAsync("search=" + s.Incident.Str("incidentNumber"));
            return list.GetProperty("items").EnumerateArray().Single();
        }

        [Fact]
        public async Task CountsDownWhileTheClockRuns_OnTheDetailAndInTheList()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");   // 8 hours

            Hours(5);

            Assert.Equal(180, Minutes(await s.RefreshAsync()));
            Assert.Equal(180, Minutes(await InListAsync(s)));
        }

        [Fact]
        public async Task GoesNegative_WhenOverdue()
        {
            var s = await Scenario.AssignedAsync(_factory, "High");

            Hours(9);

            Assert.Equal(-60, Minutes(await s.RefreshAsync()));
            Assert.Equal(-60, Minutes(await InListAsync(s)));
        }

        [Fact]
        public async Task HasNoNumber_WhenThereIsNoClockRunning()
        {
            var unassigned = await Scenario.UnassignedAsync(_factory);
            Assert.Null(Minutes(await unassigned.RefreshAsync()));

            var onHold = await Scenario.AssignedAsync(_factory, "High");
            await onHold.Assignee.SetStatusAsync(onHold.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await onHold.Assignee.SetStatusAsync(onHold.Incident, "OnHold", "waiting").ExpectAsync(HttpStatusCode.OK);
            Hours(3);
            Assert.Null(Minutes(await onHold.RefreshAsync()));

            var resolved = await Scenario.AssignedAsync(_factory, "High");
            await resolved.Assignee.SetStatusAsync(resolved.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await resolved.Assignee.SetStatusAsync(resolved.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            Assert.Null(Minutes(await resolved.RefreshAsync()));
        }
    }

    [Collection(ApiCollection.Name)]
    public class CsvExportTests
    {
        private readonly ApiFactory _factory;

        public CsvExportTests(ApiFactory factory) => _factory = factory;

        private static async Task<(HttpResponseMessage Response, string Text)> ExportAsync(TestUser who, string query = "")
        {
            var response = await who.Client.GetAsync("/api/incidents/export" + (query.Length == 0 ? "" : "?" + query));
            await response.ExpectAsync(HttpStatusCode.OK);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return (response, Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public async Task NeedsSignIn()
        {
            var response = await _factory.CreateClient().GetAsync("/api/incidents/export");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task IsACsvDownload_ThatExcelReadsAsUtf8()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            var (response, text) = await ExportAsync(manager);

            Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
            Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Contains("Number,Title,Priority,Status,Category,Reported by,Assigned to,Created (UTC),SLA,SLA due (UTC),SLA minutes left", text);
            Assert.Equal("false", response.Headers.GetValues("X-Export-Truncated").Single());
        }

        [Fact]
        public async Task HasOneRowPerIncident_WithTheSameFiltersAsTheList()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync(RoleNames.Employee);
            var mine = await reporter.ReportIncidentAsync("Export me " + Guid.NewGuid().ToString("N"));
            var other = await reporter.ReportIncidentAsync("Leave me out " + Guid.NewGuid().ToString("N"));
            await manager.AssignAsync(mine, assignee, "Critical");

            var (_, all) = await ExportAsync(manager, "reportedByMe=false&search=" + mine.Str("incidentNumber"));
            var (_, filtered) = await ExportAsync(manager, $"priority=Critical&search={mine.Str("incidentNumber")}");
            var (_, none) = await ExportAsync(manager, $"priority=Low&search={mine.Str("incidentNumber")}");

            Assert.Contains(mine.Str("incidentNumber"), all);
            Assert.DoesNotContain(other.Str("incidentNumber"), all);
            Assert.Contains(mine.Str("incidentNumber"), filtered);
            Assert.Contains(",Critical,", filtered);
            Assert.Contains(assignee.Email, filtered);
            Assert.DoesNotContain(mine.Str("incidentNumber"), none);
        }

        [Fact]
        public async Task OnlyHoldsWhatThePersonIsAllowedToSee()
        {
            var reporter = await _factory.CreateUserAsync();
            var stranger = await _factory.CreateUserAsync();
            var incident = await reporter.ReportIncidentAsync("Private " + Guid.NewGuid().ToString("N"));

            var (_, theirs) = await ExportAsync(reporter);
            var (_, strangers) = await ExportAsync(stranger);

            Assert.Contains(incident.Str("incidentNumber"), theirs);
            Assert.DoesNotContain(incident.Str("incidentNumber"), strangers);
        }

        [Fact]
        public async Task NeutralisesTextThatExcelWouldRunAsAFormula()
        {
            var reporter = await _factory.CreateUserAsync();
            var title = "=HYPERLINK(\"http://example.com\",\"click\") " + Guid.NewGuid().ToString("N");
            await reporter.ReportIncidentAsync(title);

            var (_, text) = await ExportAsync(reporter);

            Assert.DoesNotContain("\n=HYPERLINK", text);
            Assert.Contains("\"'=HYPERLINK(\"\"http://example.com\"\",\"\"click\"\")", text);
        }
    }

    public class IncidentCsvTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("plain", "plain")]
        [InlineData("a,b", "\"a,b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("line1\nline2", "\"line1\nline2\"")]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("+1", "'+1")]
        [InlineData("-1", "'-1")]
        [InlineData("@SUM(A1)", "'@SUM(A1)")]
        [InlineData("\tTabbed", "'\tTabbed")]
        public void Cell_EscapesAndNeutralises(string? input, string expected) =>
            Assert.Equal(expected, IncidentCsv.Cell(input));
    }
}
