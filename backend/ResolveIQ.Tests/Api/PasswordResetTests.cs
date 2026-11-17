using System.Net;
using System.Net.Http.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>An Admin can give someone a new password, for example after they forgot theirs.</summary>
    [Collection(ApiCollection.Name)]
    public class PasswordResetTests
    {
        private const string NewPassword = "Brand-New-Pass1!";

        private readonly ApiFactory _factory;

        public PasswordResetTests(ApiFactory factory) => _factory = factory;

        private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

        [Fact]
        public async Task TheNewPasswordWorks_AndTheOldOneNoLongerDoes()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.OK);

            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ALoginTheUserAlreadyHad_StopsWorkingAtOnce()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.OK);

            // If someone else had the old login, it is useless now.
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task TheResponse_NeverContainsAPassword()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            var response = await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(NewPassword, body);
            Assert.DoesNotContain("assword", body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(person.Email, (await response.ReadJsonAsync()).Str("email"));
        }

        [Theory]
        [InlineData("short1!")]            // too short
        [InlineData("alllowercase1!")]     // no upper-case letter
        [InlineData("NoDigitsHere!!")]     // no digit
        [InlineData("NoSymbol12345A")]     // no symbol
        public async Task AWeakPasswordIsRefused_AndTheOldOneKeepsWorking(string weak)
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            var response = await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = weak })
                .ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("NewPassword", out _));
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ABlankPasswordIsRefused()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = "" })
                .ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task OnlyAnAdminMayDoIt()
        {
            var person = await _factory.CreateUserAsync();
            var employee = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            await employee.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.Forbidden);
            await manager.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.Forbidden);

            // Nothing changed.
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ALogin_IsRequired()
        {
            var person = await _factory.CreateUserAsync();

            await _factory.CreateClient().PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task AnUnknownUser_IsNotFound()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await admin.Client.PutAsJsonAsync("/api/users/no-such-user/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ADeactivatedUser_StaysDeactivated_EvenWithTheNewPassword()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.OK);

            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task AUserLockedOutByFailedAttempts_CanLogInAtOnce_WithTheNewPassword()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            // Five wrong passwords lock the account for a few minutes.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await LoginAsync(person.Email, "wrong-password").ExpectAsync(HttpStatusCode.Unauthorized);
            }
            await LoginAsync(person.Email, person.Password).ExpectAsync(HttpStatusCode.Unauthorized); // locked, even with the right one

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.OK);

            await LoginAsync(person.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AnAdminCanResetTheirOwnPassword_AndIsSignedOutByIt()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await admin.Client.PutAsJsonAsync($"/api/users/{admin.Id}/password", new { newPassword = NewPassword })
                .ExpectAsync(HttpStatusCode.OK);

            await admin.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);
            await LoginAsync(admin.Email, NewPassword).ExpectAsync(HttpStatusCode.OK);
        }
    }
}
