using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// A refresh token is a single-use ticket for getting a new short-lived login token without typing the password
    /// again. These tests cover the ways it must work, and the ways it must refuse.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class RefreshTokenTests
    {
        private readonly ApiFactory _factory;

        public RefreshTokenTests(ApiFactory factory) => _factory = factory;

        private async Task<(string Token, string Refresh)> SignInAsync(TestUser user)
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email = user.Email, password = user.Password }).ExpectAsync(HttpStatusCode.OK);
            var body = await response.ReadJsonAsync();
            return (body.Str("token"), body.Str("refreshToken"));
        }

        private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken });

        // ---- the normal path ----

        [Fact]
        public async Task SigningIn_HandsOutARefreshToken_ADifferentOneEachTime()
        {
            var person = await _factory.CreateUserAsync();

            var first = await SignInAsync(person);
            var second = await SignInAsync(person);

            Assert.False(string.IsNullOrWhiteSpace(first.Refresh));
            Assert.NotEqual(first.Refresh, second.Refresh);
        }

        [Fact]
        public async Task Registering_AlsoHandsOutOne()
        {
            var email = $"new.{Guid.NewGuid():N}@example.com";

            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
                new { email, password = ApiFactory.StandardPassword }).ExpectAsync(HttpStatusCode.OK);

            Assert.False(string.IsNullOrWhiteSpace((await response.ReadJsonAsync()).Str("refreshToken")));
        }

        [Fact]
        public async Task ARefreshToken_GivesANewLoginToken_ThatWorks_AndANewRefreshToken()
        {
            var person = await _factory.CreateUserAsync(RoleNames.Manager);
            var (_, refresh) = await SignInAsync(person);

            var response = await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.OK);
            var body = await response.ReadJsonAsync();

            Assert.NotEqual(refresh, body.Str("refreshToken"));          // the old ticket was swapped for a new one
            Assert.Equal(person.Email, body.Str("email"));

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Str("token"));
            await client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK);   // a Manager's own access, still intact
        }

        [Fact]
        public async Task TheNewRefreshToken_CanBeUsedInTurn_ForAsLongAsThePersonStaysActive()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            for (var i = 0; i < 3; i++)
            {
                var body = await (await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
                refresh = body.Str("refreshToken");
                _factory.Time.Advance(TimeSpan.FromMinutes(1));
            }
        }

        // ---- single use, and theft ----

        [Fact]
        public async Task ATicketWorksOnce_UsingItAgainLaterIsRefused_AndCancelsEverythingThePersonHolds()
        {
            var person = await _factory.CreateUserAsync();
            var (_, original) = await SignInAsync(person);
            var rotated = (await (await RefreshAsync(original).ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync()).Str("refreshToken");

            _factory.Time.Advance(TimeSpan.FromMinutes(5));

            // Someone presents the old, used-up ticket (a copy was stolen): refused...
            await RefreshAsync(original).ExpectAsync(HttpStatusCode.Unauthorized);

            // ...and the genuine newer ticket is cancelled too, so the thief gains nothing and the owner signs in again.
            await RefreshAsync(rotated).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task TwoTabsUsingTheSameTicketAtOnce_OnlyOneWins_AndTheWinnersNewTicketSurvives()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            var calls = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RefreshAsync(refresh)));

            Assert.Equal(1, calls.Count(c => c.StatusCode == HttpStatusCode.OK));
            Assert.Equal(3, calls.Count(c => c.StatusCode == HttpStatusCode.Unauthorized));

            // The same moment is a race, not theft, so the winner's new ticket was not cancelled.
            var winner = await calls.Single(c => c.StatusCode == HttpStatusCode.OK).ReadJsonAsync();
            await RefreshAsync(winner.Str("refreshToken")).ExpectAsync(HttpStatusCode.OK);
        }

        // ---- when a ticket must stop working ----

        [Fact]
        public async Task ATicketExpires_AfterTheConfiguredDays()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            _factory.Time.Advance(TimeSpan.FromDays(8));

            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ATicketStillWorks_JustBeforeItExpires()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            _factory.Time.Advance(TimeSpan.FromDays(6));

            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ChangingYourPassword_EndsEveryOtherDevicesTicket_ButTheFreshOneWorks()
        {
            var person = await _factory.CreateUserAsync();
            var (_, otherDevice) = await SignInAsync(person);

            var changed = await person.Client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = person.Password, newPassword = "Brand-New-Pass1!" }).ExpectAsync(HttpStatusCode.OK);
            var fresh = (await changed.ReadJsonAsync()).Str("refreshToken");

            await RefreshAsync(otherDevice).ExpectAsync(HttpStatusCode.Unauthorized);
            await RefreshAsync(fresh).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AResetPasswordLink_EndsEveryTicket()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            await _factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email = person.Email });
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password",
                new { email = person.Email, token = _factory.Emails.Latest(person.Email).Token, newPassword = "Brand-New-Pass1!" })
                .ExpectAsync(HttpStatusCode.OK);

            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ChangingARole_EndsTheTicket_SoTheOldRoleCannotBeRenewed()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync(RoleNames.Manager);
            var (_, refresh) = await SignInAsync(person);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Employee" }).ExpectAsync(HttpStatusCode.OK);

            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ADeactivatedPerson_CannotRefresh_AndStaysOutAfterReactivationUntilTheySignInAgain()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);
            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.Unauthorized);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = true }).ExpectAsync(HttpStatusCode.OK);
            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        // ---- signing out, and bad input ----

        [Fact]
        public async Task SigningOut_CancelsTheTicket()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            await _factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh })
                .ExpectAsync(HttpStatusCode.NoContent);

            await RefreshAsync(refresh).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task SigningOut_WithAnUnknownTicket_IsHarmless()
        {
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new { refreshToken = "never-issued" })
                .ExpectAsync(HttpStatusCode.NoContent);
        }

        [Theory]
        [InlineData("never-issued")]
        [InlineData("a")]
        public async Task AMadeUpTicket_IsRefused(string made)
        {
            await RefreshAsync(made).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ABlankOrMissingTicket_IsABadRequest()
        {
            await RefreshAsync("").ExpectAsync(HttpStatusCode.BadRequest);
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh-token", new { }).ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task ATicketCannotBeUsedAsALoginToken()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refresh);

            await client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);
        }

        // ---- what is stored ----

        [Fact]
        public async Task TheDatabase_KeepsOnlyAFingerprint_NeverTheTicketItself()
        {
            var person = await _factory.CreateUserAsync();
            var (_, refresh) = await SignInAsync(person);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == person.Id).ToListAsync();

            Assert.NotEmpty(rows);
            Assert.DoesNotContain(rows, r => r.TokenHash == refresh);
            Assert.All(rows, r => Assert.Equal(64, r.TokenHash.Length));     // a SHA-256 fingerprint in hex
        }

        [Fact]
        public async Task TheLoginToken_NowLastsFifteenMinutes_NotAnHour()
        {
            var person = await _factory.CreateUserAsync();
            var (token, _) = await SignInAsync(person);

            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
            var lifetime = json.RootElement.GetProperty("exp").GetInt64() - DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Assert.InRange(lifetime, 14 * 60, 15 * 60 + 5);
        }
    }
}
