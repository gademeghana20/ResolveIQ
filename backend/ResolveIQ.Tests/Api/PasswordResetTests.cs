using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// Passwords are never typed for someone else. People change their own, or choose a new one through a
    /// one-time link sent to their email (when they forgot it, or when an Admin asks for it).
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class PasswordResetTests
    {
        private const string NewPassword = "Brand-New-Pass1!";

        private readonly ApiFactory _factory;

        public PasswordResetTests(ApiFactory factory) => _factory = factory;

        private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

        private Task<HttpResponseMessage> ForgotAsync(string email) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email });

        private Task<HttpResponseMessage> ResetAsync(string email, string token, string newPassword) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password", new { email, token, newPassword });

        // ---- "Forgot password" ----

        [Fact]
        public async Task ForgotPassword_EmailsALink_ThatLetsThemChooseANewPassword()
        {
            var person = await _factory.CreateUserAsync();

            await ForgotAsync(person.Email).ExpectAsync(HttpStatusCode.OK);

            var email = _factory.Emails.Latest(person.Email);
            Assert.Contains("reset", email.Subject, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("http://localhost:4200/reset-password?email=", email.Link);

            await ResetAsync(person.Email, email.Token, NewPassword).ExpectAsync(HttpStatusCode.OK);
            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ForgotPassword_GivesTheSameAnswer_ForAnAddressThatHasNoAccount()
        {
            var person = await _factory.CreateUserAsync();
            var stranger = $"nobody.{Guid.NewGuid():N}@example.com";

            var known = await (await ForgotAsync(person.Email).ExpectAsync(HttpStatusCode.OK)).Content.ReadAsStringAsync();
            var unknown = await (await ForgotAsync(stranger).ExpectAsync(HttpStatusCode.OK)).Content.ReadAsStringAsync();

            Assert.Equal(known, unknown);                 // nothing reveals which addresses are registered
            Assert.Empty(_factory.Emails.To(stranger));   // and nothing was sent to a stranger
        }

        [Fact]
        public async Task ForgotPassword_SendsNothingToADeactivatedUser()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);

            await ForgotAsync(person.Email).ExpectAsync(HttpStatusCode.OK);

            Assert.Empty(_factory.Emails.To(person.Email));
        }

        [Fact]
        public async Task ForgotPassword_RefusesSomethingThatIsNotAnEmailAddress()
        {
            await ForgotAsync("not-an-email").ExpectAsync(HttpStatusCode.BadRequest);
            await ForgotAsync("").ExpectAsync(HttpStatusCode.BadRequest);
        }

        // ---- using the link ----

        [Fact]
        public async Task TheLink_WorksOnlyOnce()
        {
            var person = await _factory.CreateUserAsync();
            await ForgotAsync(person.Email);
            var token = _factory.Emails.Latest(person.Email).Token;

            await ResetAsync(person.Email, token, NewPassword).ExpectAsync(HttpStatusCode.OK);

            // Using it again, even to set the same password, is refused: the token changed with the password.
            var again = await ResetAsync(person.Email, token, "Another-Pass2!").ExpectAsync(HttpStatusCode.BadRequest);
            Assert.True((await again.ReadJsonAsync()).GetProperty("errors").TryGetProperty("Token", out _));
            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ALoginTheUserAlreadyHad_StopsWorkingAtOnce()
        {
            var person = await _factory.CreateUserAsync();
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);

            await ForgotAsync(person.Email);
            await ResetAsync(person.Email, _factory.Emails.Latest(person.Email).Token, NewPassword).ExpectAsync(HttpStatusCode.OK);

            // If someone else had the old login, it is useless now.
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("not-a-real-token")]
        [InlineData("")]
        public async Task AMadeUpToken_IsRefused(string token)
        {
            var person = await _factory.CreateUserAsync();

            await ResetAsync(person.Email, token, NewPassword).ExpectAsync(HttpStatusCode.BadRequest);

            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);   // nothing changed
        }

        [Fact]
        public async Task ATokenForOnePerson_DoesNotWorkForAnother()
        {
            var one = await _factory.CreateUserAsync();
            var other = await _factory.CreateUserAsync();
            await ForgotAsync(one.Email);

            await ResetAsync(other.Email, _factory.Emails.Latest(one.Email).Token, NewPassword)
                .ExpectAsync(HttpStatusCode.BadRequest);

            await LoginAsync(other.Email, other.Password).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AnUnknownAddress_GetsTheSameRefusalAsABadToken()
        {
            var person = await _factory.CreateUserAsync();
            await ForgotAsync(person.Email);
            var token = _factory.Emails.Latest(person.Email).Token;

            var bad = await (await ResetAsync(person.Email, "wrong", NewPassword).ExpectAsync(HttpStatusCode.BadRequest))
                .Content.ReadAsStringAsync();
            var stranger = await (await ResetAsync($"nobody.{Guid.NewGuid():N}@example.com", token, NewPassword)
                .ExpectAsync(HttpStatusCode.BadRequest)).Content.ReadAsStringAsync();

            // Same shape and wording: nothing here tells an attacker whether an address has an account.
            Assert.Contains("not valid any more", bad);
            Assert.Contains("not valid any more", stranger);
        }

        [Theory]
        [InlineData("short1!")]            // too short
        [InlineData("alllowercase1!")]     // no upper-case letter
        [InlineData("NoDigitsHere!!")]     // no digit
        [InlineData("NoSymbol12345A")]     // no symbol
        public async Task AWeakNewPassword_IsRefused_AndTheLinkStillWorksAfterwards(string weak)
        {
            var person = await _factory.CreateUserAsync();
            await ForgotAsync(person.Email);
            var token = _factory.Emails.Latest(person.Email).Token;

            var response = await ResetAsync(person.Email, token, weak).ExpectAsync(HttpStatusCode.BadRequest);
            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("NewPassword", out _));

            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
            await ResetAsync(person.Email, token, NewPassword).ExpectAsync(HttpStatusCode.OK);   // a typo does not waste the link
        }

        [Fact]
        public async Task AUserLockedOutByFailedAttempts_CanLogInAtOnce_AfterChoosingANewPassword()
        {
            var person = await _factory.CreateUserAsync();
            for (var i = 0; i < 6; i++)
            {
                await LoginAsync(person.Email, "wrong-password");
            }

            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.Unauthorized);   // locked

            await ForgotAsync(person.Email);
            await ResetAsync(person.Email, _factory.Emails.Latest(person.Email).Token, NewPassword).ExpectAsync(HttpStatusCode.OK);

            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task TheResponses_NeverContainAPasswordOrToken()
        {
            var person = await _factory.CreateUserAsync();

            var forgot = await (await ForgotAsync(person.Email)).Content.ReadAsStringAsync();
            await ForgotAsync(person.Email);
            var token = _factory.Emails.Latest(person.Email).Token;
            var reset = await (await ResetAsync(person.Email, token, NewPassword)).Content.ReadAsStringAsync();

            foreach (var body in new[] { forgot, reset })
            {
                Assert.DoesNotContain(NewPassword, body);
                Assert.DoesNotContain(token, body);
            }
        }

        // ---- changing your own password ----

        private static async Task<HttpResponseMessage> ChangeAsync(TestUser user, string current, string next) =>
            await user.Client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = current, newPassword = next });

        [Fact]
        public async Task ChangingYourPassword_NeedsTheCurrentOne_AndTheNewOneThenWorks()
        {
            var person = await _factory.CreateUserAsync();

            await ChangeAsync(person, person.Password, NewPassword).ExpectAsync(HttpStatusCode.OK);

            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ChangingYourPassword_KeepsYouSignedIn_WithAFreshToken_ButEndsOtherLogins()
        {
            var person = await _factory.CreateUserAsync();
            var elsewhere = _factory.CreateClient();       // the same person, signed in on another device
            elsewhere.DefaultRequestHeaders.Authorization = person.Client.DefaultRequestHeaders.Authorization;

            var response = await ChangeAsync(person, person.Password, NewPassword).ExpectAsync(HttpStatusCode.OK);
            var fresh = (await response.ReadJsonAsync()).Str("token");

            // The other device is signed out...
            await elsewhere.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);

            // ...but the token that came back works, so this person is not thrown out of the app.
            person.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ChangingYourPassword_WithAWrongCurrentPassword_IsRefused_AndNothingChanges()
        {
            var person = await _factory.CreateUserAsync();

            var response = await ChangeAsync(person, "not-my-password", NewPassword).ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("CurrentPassword", out _));
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GuessingTheCurrentPasswordTooOften_LocksTheAccount_LikeTheLoginScreen()
        {
            var person = await _factory.CreateUserAsync();

            for (var i = 0; i < 6; i++)
            {
                await ChangeAsync(person, "not-my-password", NewPassword);
            }

            // A locked account is signed out of everything: the right current password gets nowhere, and neither does signing in.
            await ChangeAsync(person, person.Password, NewPassword).ExpectAsync(HttpStatusCode.Unauthorized);
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("short1!")]
        [InlineData("alllowercase1!")]
        [InlineData("NoDigitsHere!!")]
        [InlineData("NoSymbol12345A")]
        public async Task ChangingYourPassword_ToAWeakOne_IsRefused_WithTheRulesItBroke(string weak)
        {
            var person = await _factory.CreateUserAsync();

            var response = await ChangeAsync(person, person.Password, weak).ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("NewPassword", out _));
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ChangingYourPassword_NeedsALogin()
        {
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = "x", newPassword = NewPassword }).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        // ---- an Admin asking for a reset email ----

        [Fact]
        public async Task AnAdmin_CanSendSomeoneAResetEmail_WithoutEverSeeingAPassword()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            var response = await admin.Client.PostAsync($"/api/users/{person.Id}/password-reset-email", null)
                .ExpectAsync(HttpStatusCode.OK);

            Assert.DoesNotContain("assword", await response.Content.ReadAsStringAsync());
            var email = _factory.Emails.Latest(person.Email);
            Assert.Contains("administrator", email.Body);

            await ResetAsync(person.Email, email.Token, NewPassword).ExpectAsync(HttpStatusCode.OK);
            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task SendingTheEmail_ChangesNothingUntilThePersonFollowsTheLink()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            await admin.Client.PostAsync($"/api/users/{person.Id}/password-reset-email", null).ExpectAsync(HttpStatusCode.OK);

            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task OnlyAnAdminMayAskForIt()
        {
            var person = await _factory.CreateUserAsync();
            var employee = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            await employee.Client.PostAsync($"/api/users/{person.Id}/password-reset-email", null).ExpectAsync(HttpStatusCode.Forbidden);
            await manager.Client.PostAsync($"/api/users/{person.Id}/password-reset-email", null).ExpectAsync(HttpStatusCode.Forbidden);
            await _factory.CreateClient().PostAsync($"/api/users/{person.Id}/password-reset-email", null).ExpectAsync(HttpStatusCode.Unauthorized);

            Assert.Empty(_factory.Emails.To(person.Email));
        }

        [Fact]
        public async Task AnUnknownUser_IsNotFound()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await admin.Client.PostAsync("/api/users/no-such-user/password-reset-email", null).ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ADeactivatedUser_GetsNoEmail_AndTheAdminIsToldWhy()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);

            var response = await admin.Client.PostAsync($"/api/users/{person.Id}/password-reset-email", null)
                .ExpectAsync(HttpStatusCode.BadRequest);

            Assert.Contains("deactivated", await response.Content.ReadAsStringAsync());
            Assert.Empty(_factory.Emails.To(person.Email));
        }

        [Fact]
        public async Task TheOldAdminEndpoint_ThatSetAPasswordForSomeoneElse_NoLongerExists()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            var response = await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword });

            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
        }
    }
}
