using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Tests.Validation;

namespace ResolveIQ.Tests.Api
{
    // ---- the app with small limits, so the limits can be reached quickly ----

    public class SmallAttachmentLimitsApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Attachments:MaxFileBytes"] = "2048",
            ["Attachments:MaxFilesPerIncident"] = "3",
            ["Attachments:MaxFilesPerComment"] = "2"
        };
    }

    /// <summary>A public demo site: uploads are off unless someone turns them on.</summary>
    public class DemoSiteApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Demo:ProtectAccounts"] = "true"
        };
    }

    internal static class AttachmentHelpers
    {
        public static MultipartFormDataContent Form(string fileName, byte[] bytes, Guid? commentId = null, string claimedType = "application/octet-stream")
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(claimedType);
            form.Add(file, "file", fileName);
            if (commentId is Guid id)
            {
                form.Add(new StringContent(id.ToString()), "commentId");
            }

            return form;
        }

        public static Task<HttpResponseMessage> AttachAsync(
            this TestUser who, JsonElement incident, string fileName, byte[] bytes, Guid? commentId = null, string claimedType = "application/octet-stream") =>
            who.Client.PostAsync($"/api/incidents/{incident.GetGuid("id")}/attachments", Form(fileName, bytes, commentId, claimedType));

        public static async Task<JsonElement> AttachedAsync(this TestUser who, JsonElement incident, string fileName, byte[] bytes, Guid? commentId = null) =>
            await (await who.AttachAsync(incident, fileName, bytes, commentId).ExpectAsync(HttpStatusCode.Created)).ReadJsonAsync();

        public static async Task<JsonElement> ListAttachmentsAsync(this TestUser who, JsonElement incident) =>
            await (await who.Client.GetAsync($"/api/incidents/{incident.GetGuid("id")}/attachments").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();

        public static async Task<Guid> CommentAsync(this TestUser who, JsonElement incident, string text = "See the picture.")
        {
            var response = await who.Client.PostAsJsonAsync($"/api/incidents/{incident.GetGuid("id")}/comments", new { text });
            await response.ExpectAsync(HttpStatusCode.Created);
            return (await response.ReadJsonAsync()).GetGuid("id");
        }
    }

    /// <summary>Files on incidents and comments: who may add, see and remove them, and what is accepted.</summary>
    [Collection(ApiCollection.Name)]
    public class AttachmentTests
    {
        private readonly ApiFactory _factory;

        public AttachmentTests(ApiFactory factory) => _factory = factory;

        private sealed record Scene(TestUser Reporter, TestUser Manager, TestUser Assignee, JsonElement Incident);

        private async Task<Scene> AssignedIncidentAsync()
        {
            var reporter = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var assignee = await _factory.CreateUserAsync();
            var incident = await reporter.ReportIncidentAsync("Report will not open");
            incident = await manager.AssignAsync(incident, assignee);
            return new Scene(reporter, manager, assignee, incident);
        }

        // ---- attaching and downloading ----

        [Fact]
        public async Task AFileCanBeAttached_ListedAndDownloadedBackExactlyAsSent()
        {
            var s = await AssignedIncidentAsync();

            // The sender claims it is a PDF; the app trusts neither that nor the name's case, only the real bytes.
            var attached = await s.Reporter.AttachAsync(s.Incident, "Screenshot.PNG", AttachmentRulesTests.Png, claimedType: "application/pdf")
                .ExpectAsync(HttpStatusCode.Created);
            var body = await attached.ReadJsonAsync();

            Assert.Equal("Screenshot.PNG", body.Str("fileName"));
            Assert.Equal("image/png", body.Str("contentType"));
            Assert.Equal(AttachmentRulesTests.Png.Length, body.GetProperty("sizeBytes").GetInt64());
            Assert.Equal(s.Reporter.Email, body.Str("uploadedByEmail"));
            Assert.Equal(JsonValueKind.Null, body.GetProperty("commentId").ValueKind);
            Assert.True(body.GetProperty("canRemove").GetBoolean());

            var list = await s.Manager.ListAttachmentsAsync(s.Incident);
            Assert.Equal(1, list.GetArrayLength());
            Assert.Equal(body.Str("id"), list[0].Str("id"));

            var download = await s.Assignee.Client.GetAsync($"/api/attachments/{body.Str("id")}/download").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(AttachmentRulesTests.Png, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);

            // Always a download, never shown inside the page, and browsers are told not to guess a different type.
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Contains("no-store", download.Headers.CacheControl?.ToString());
        }

        [Fact]
        public async Task TheNameIsCleaned_SoAFolderPathOrOddCharactersNeverGetThrough()
        {
            var s = await AssignedIncidentAsync();

            var body = await s.Reporter.AttachedAsync(s.Incident, "..\\..\\evil<name>.png", AttachmentRulesTests.Png);

            Assert.Equal("evilname.png", body.Str("fileName"));
        }

        [Fact]
        public async Task AttachingAFile_IsWrittenToTheHistory_AndTheOtherPeopleAreTold()
        {
            var s = await AssignedIncidentAsync();

            await s.Reporter.AttachedAsync(s.Incident, "log.txt", AttachmentRulesTests.Text);

            var history = await (await s.Manager.Client.GetAsync($"/api/incidents/{s.Incident.GetGuid("id")}/history")).ReadJsonAsync();
            var line = history.EnumerateArray().Last(h => h.Str("fieldChanged") == "AttachmentAdded");
            Assert.Equal("log.txt", line.Str("newValue"));
            Assert.Equal(s.Reporter.Email, line.Str("changedBy"));

            var bell = await (await s.Assignee.Client.GetAsync("/api/notifications")).ReadJsonAsync();
            Assert.Contains(bell.GetProperty("items").EnumerateArray(), n => n.Str("message").Contains("attached a file"));

            // The person who attached it is not told about their own action.
            var own = await (await s.Reporter.Client.GetAsync("/api/notifications")).ReadJsonAsync();
            Assert.DoesNotContain(own.GetProperty("items").EnumerateArray(), n => n.Str("message").Contains("attached a file"));
        }

        // ---- what is refused ----

        [Theory]
        [InlineData("program.exe")]
        [InlineData("page.html")]
        [InlineData("picture.svg")]
        public async Task AKindOfFileThatIsNotAllowed_IsRefused(string name)
        {
            var s = await AssignedIncidentAsync();

            var response = await s.Reporter.AttachAsync(s.Incident, name, AttachmentRulesTests.Png).ExpectAsync(HttpStatusCode.BadRequest);

            Assert.Contains("not allowed", await response.Content.ReadAsStringAsync());
            Assert.Equal(0, (await s.Reporter.ListAttachmentsAsync(s.Incident)).GetArrayLength());
        }

        [Fact]
        public async Task ADisguisedFile_IsRefused()
        {
            var s = await AssignedIncidentAsync();

            var response = await s.Reporter.AttachAsync(s.Incident, "cute.png", Encoding.ASCII.GetBytes("<script>alert(1)</script>"))
                .ExpectAsync(HttpStatusCode.BadRequest);

            Assert.Contains("do not match", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AFileThatIsTooBig_IsRefused_WithTheLimitInTheMessage()
        {
            var s = await AssignedIncidentAsync();
            var big = new byte[(5 * 1024 * 1024) + 1];
            Array.Copy(AttachmentRulesTests.Png, big, AttachmentRulesTests.Png.Length);

            var response = await s.Reporter.AttachAsync(s.Incident, "huge.png", big).ExpectAsync(HttpStatusCode.BadRequest);

            Assert.Contains("too big", await response.Content.ReadAsStringAsync());
            Assert.Contains("5 MB", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AnEmptyFile_AndNoFileAtAll_AreRefused()
        {
            var s = await AssignedIncidentAsync();

            await s.Reporter.AttachAsync(s.Incident, "empty.txt", Array.Empty<byte>()).ExpectAsync(HttpStatusCode.BadRequest);

            var noFile = await s.Reporter.Client.PostAsync(
                $"/api/incidents/{s.Incident.GetGuid("id")}/attachments", new MultipartFormDataContent());
            Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        }

        [Fact]
        public async Task AClosedIncident_TakesNoMoreFiles_AndItsFilesStay()
        {
            var s = await AssignedIncidentAsync();
            var attached = await s.Reporter.AttachedAsync(s.Incident, "before.txt", AttachmentRulesTests.Text);

            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            await s.Assignee.SetStatusAsync(s.Incident, "Resolved").ExpectAsync(HttpStatusCode.OK);
            await s.Reporter.SetStatusAsync(s.Incident, "Closed").ExpectAsync(HttpStatusCode.OK);

            var more = await s.Reporter.AttachAsync(s.Incident, "after.txt", AttachmentRulesTests.Text).ExpectAsync(HttpStatusCode.BadRequest);
            Assert.Contains("closed", await more.Content.ReadAsStringAsync());

            var remove = await s.Reporter.Client.DeleteAsync($"/api/attachments/{attached.Str("id")}").ExpectAsync(HttpStatusCode.BadRequest);
            Assert.Contains("closed", await remove.Content.ReadAsStringAsync());

            // The record stays readable, and nobody is offered a "remove" for it.
            var list = await s.Manager.ListAttachmentsAsync(s.Incident);
            Assert.Equal(1, list.GetArrayLength());
            Assert.False(list[0].GetProperty("canRemove").GetBoolean());
            await s.Manager.Client.GetAsync($"/api/attachments/{attached.Str("id")}/download").ExpectAsync(HttpStatusCode.OK);
        }

        // ---- who may see a file ----

        [Fact]
        public async Task SomeoneWhoCannotSeeTheIncident_CannotListUploadDownloadOrRemove_AndCannotTellItExists()
        {
            var s = await AssignedIncidentAsync();
            var attached = await s.Reporter.AttachedAsync(s.Incident, "private.txt", AttachmentRulesTests.Text);
            var stranger = await _factory.CreateUserAsync();
            var id = s.Incident.GetGuid("id");

            await stranger.Client.GetAsync($"/api/incidents/{id}/attachments").ExpectAsync(HttpStatusCode.NotFound);
            await stranger.AttachAsync(s.Incident, "mine.txt", AttachmentRulesTests.Text).ExpectAsync(HttpStatusCode.NotFound);
            await stranger.Client.GetAsync($"/api/attachments/{attached.Str("id")}/download").ExpectAsync(HttpStatusCode.NotFound);
            await stranger.Client.DeleteAsync($"/api/attachments/{attached.Str("id")}").ExpectAsync(HttpStatusCode.NotFound);

            // A file that does not exist at all gives the very same answer.
            await stranger.Client.GetAsync($"/api/attachments/{Guid.NewGuid()}/download").ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ManagersAndAdmins_SeeEveryonesFiles()
        {
            var s = await AssignedIncidentAsync();
            var attached = await s.Reporter.AttachedAsync(s.Incident, "x.txt", AttachmentRulesTests.Text);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await admin.Client.GetAsync($"/api/attachments/{attached.Str("id")}/download").ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(1, (await admin.ListAttachmentsAsync(s.Incident)).GetArrayLength());
        }

        // ---- files on comments ----

        [Fact]
        public async Task AFileCanBeAttachedToYourOwnComment_AndShowsAgainstThatComment()
        {
            var s = await AssignedIncidentAsync();
            var commentId = await s.Reporter.CommentAsync(s.Incident);

            var body = await s.Reporter.AttachedAsync(s.Incident, "shot.png", AttachmentRulesTests.Png, commentId);

            Assert.Equal(commentId, body.GetGuid("commentId"));
            var list = await s.Assignee.ListAttachmentsAsync(s.Incident);
            Assert.Equal(commentId, list[0].GetGuid("commentId"));

            // A file that comes with a comment does not send a second notification on top of the comment's own.
            var bell = await (await s.Assignee.Client.GetAsync("/api/notifications")).ReadJsonAsync();
            Assert.DoesNotContain(bell.GetProperty("items").EnumerateArray(), n => n.Str("message").Contains("attached a file"));
        }

        [Fact]
        public async Task OnlyTheAuthorOfAComment_CanAttachToIt()
        {
            var s = await AssignedIncidentAsync();
            var commentId = await s.Reporter.CommentAsync(s.Incident);

            var response = await s.Assignee.AttachAsync(s.Incident, "sneaky.txt", AttachmentRulesTests.Text, commentId)
                .ExpectAsync(HttpStatusCode.Forbidden);

            Assert.Contains("wrote a comment", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ACommentOfAnotherIncident_CannotBeUsed()
        {
            var s = await AssignedIncidentAsync();
            var other = await s.Reporter.ReportIncidentAsync("Another problem");
            var othersComment = await s.Reporter.CommentAsync(other);

            await s.Reporter.AttachAsync(s.Incident, "wrong.txt", AttachmentRulesTests.Text, othersComment).ExpectAsync(HttpStatusCode.BadRequest);
            await s.Reporter.AttachAsync(s.Incident, "wrong.txt", AttachmentRulesTests.Text, Guid.NewGuid()).ExpectAsync(HttpStatusCode.BadRequest);
        }

        // ---- removing ----

        [Fact]
        public async Task TheUploaderCanRemoveTheirFile_AndItIsWrittenToTheHistory()
        {
            var s = await AssignedIncidentAsync();
            var attached = await s.Reporter.AttachedAsync(s.Incident, "oops.txt", AttachmentRulesTests.Text);

            await s.Reporter.Client.DeleteAsync($"/api/attachments/{attached.Str("id")}").ExpectAsync(HttpStatusCode.NoContent);

            Assert.Equal(0, (await s.Reporter.ListAttachmentsAsync(s.Incident)).GetArrayLength());
            await s.Reporter.Client.GetAsync($"/api/attachments/{attached.Str("id")}/download").ExpectAsync(HttpStatusCode.NotFound);

            var history = await (await s.Manager.Client.GetAsync($"/api/incidents/{s.Incident.GetGuid("id")}/history")).ReadJsonAsync();
            var line = history.EnumerateArray().Last(h => h.Str("fieldChanged") == "AttachmentRemoved");
            Assert.Equal("oops.txt", line.Str("oldValue"));
        }

        [Fact]
        public async Task AnotherParticipant_CannotRemoveYourFile_ButAManagerCan()
        {
            var s = await AssignedIncidentAsync();
            var attached = await s.Reporter.AttachedAsync(s.Incident, "mine.txt", AttachmentRulesTests.Text);

            // The assignee can see it but did not attach it, and is not a manager.
            var list = await s.Assignee.ListAttachmentsAsync(s.Incident);
            Assert.False(list[0].GetProperty("canRemove").GetBoolean());
            await s.Assignee.Client.DeleteAsync($"/api/attachments/{attached.Str("id")}").ExpectAsync(HttpStatusCode.Forbidden);
            Assert.Equal(1, (await s.Reporter.ListAttachmentsAsync(s.Incident)).GetArrayLength());

            var managerList = await s.Manager.ListAttachmentsAsync(s.Incident);
            Assert.True(managerList[0].GetProperty("canRemove").GetBoolean());
            await s.Manager.Client.DeleteAsync($"/api/attachments/{attached.Str("id")}").ExpectAsync(HttpStatusCode.NoContent);
        }
    }

    /// <summary>The limits on how many files, and how big, using small limits so they are quick to reach.</summary>
    public class AttachmentLimitTests : IClassFixture<SmallAttachmentLimitsApiFactory>
    {
        private readonly SmallAttachmentLimitsApiFactory _factory;

        public AttachmentLimitTests(SmallAttachmentLimitsApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TheSizeLimit_FollowsTheSetting()
        {
            var user = await _factory.CreateUserAsync();
            var incident = await user.ReportIncidentAsync();
            var tooBig = new byte[2049];
            Array.Copy(AttachmentRulesTests.Png, tooBig, AttachmentRulesTests.Png.Length);

            await user.AttachAsync(incident, "a.png", tooBig).ExpectAsync(HttpStatusCode.BadRequest);

            var justRight = new byte[2048];
            Array.Copy(AttachmentRulesTests.Png, justRight, AttachmentRulesTests.Png.Length);
            await user.AttachAsync(incident, "b.png", justRight).ExpectAsync(HttpStatusCode.Created);
        }

        [Fact]
        public async Task AnIncident_HoldsOnlyAsManyFilesAsTheLimitSays()
        {
            var user = await _factory.CreateUserAsync();
            var incident = await user.ReportIncidentAsync();

            for (var i = 1; i <= 3; i++)
            {
                await user.AttachAsync(incident, $"file{i}.txt", AttachmentRulesTests.Text).ExpectAsync(HttpStatusCode.Created);
            }

            var fourth = await user.AttachAsync(incident, "file4.txt", AttachmentRulesTests.Text).ExpectAsync(HttpStatusCode.BadRequest);
            Assert.Contains("most files", await fourth.Content.ReadAsStringAsync());

            // Removing one makes room again.
            var list = await user.ListAttachmentsAsync(incident);
            await user.Client.DeleteAsync($"/api/attachments/{list[0].Str("id")}").ExpectAsync(HttpStatusCode.NoContent);
            await user.AttachAsync(incident, "file4.txt", AttachmentRulesTests.Text).ExpectAsync(HttpStatusCode.Created);
        }

        [Fact]
        public async Task AComment_CarriesOnlyAsManyFilesAsTheLimitSays()
        {
            var user = await _factory.CreateUserAsync();
            var incident = await user.ReportIncidentAsync();
            var commentId = await user.CommentAsync(incident);

            await user.AttachAsync(incident, "one.txt", AttachmentRulesTests.Text, commentId).ExpectAsync(HttpStatusCode.Created);
            await user.AttachAsync(incident, "two.txt", AttachmentRulesTests.Text, commentId).ExpectAsync(HttpStatusCode.Created);

            var third = await user.AttachAsync(incident, "three.txt", AttachmentRulesTests.Text, commentId).ExpectAsync(HttpStatusCode.BadRequest);
            Assert.Contains("at most 2", await third.Content.ReadAsStringAsync());
        }
    }

    /// <summary>Settings the web app reads before it shows the attach controls.</summary>
    public class AttachmentSettingsEndpointTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;

        public AttachmentSettingsEndpointTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task OrdinarySite_ReportsUploadsOn_WithTheLimitsAndTheAllowedKinds()
        {
            var user = await _factory.CreateUserAsync();

            var settings = await (await user.Client.GetAsync("/api/attachments/settings").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();

            Assert.True(settings.GetProperty("enabled").GetBoolean());
            Assert.Equal(5L * 1024 * 1024, settings.GetProperty("maxFileBytes").GetInt64());
            Assert.Equal(20, settings.GetProperty("maxFilesPerIncident").GetInt32());
            Assert.Equal(5, settings.GetProperty("maxFilesPerComment").GetInt32());
            var kinds = settings.GetProperty("allowedExtensions").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(".png", kinds);
            Assert.Contains(".pdf", kinds);
            Assert.DoesNotContain(".exe", kinds);
            Assert.DoesNotContain(".svg", kinds);
        }
    }

    public class DemoSiteAttachmentTests : IClassFixture<DemoSiteApiFactory>
    {
        private readonly DemoSiteApiFactory _factory;

        public DemoSiteAttachmentTests(DemoSiteApiFactory factory) => _factory = factory;

        [Fact]
        public async Task OnAPublicDemoSite_UploadsAreOffByDefault()
        {
            var user = await _factory.CreateUserAsync();
            var incident = await user.ReportIncidentAsync();

            var settings = await (await user.Client.GetAsync("/api/attachments/settings")).ReadJsonAsync();
            Assert.False(settings.GetProperty("enabled").GetBoolean());

            var response = await user.AttachAsync(incident, "shot.png", AttachmentRulesTests.Png).ExpectAsync(HttpStatusCode.BadRequest);
            Assert.Contains("switched off", await response.Content.ReadAsStringAsync());

            // Listing still works (there is just nothing there), so existing files would stay visible.
            Assert.Equal(0, (await user.ListAttachmentsAsync(incident)).GetArrayLength());
        }
    }
}
