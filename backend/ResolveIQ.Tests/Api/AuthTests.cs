using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class AuthTests
    {
        private readonly ApiFactory _factory;

        public AuthTests(ApiFactory factory) => _factory = factory;

        private static string NewEmail() => $"person.{Guid.NewGuid():N}@example.com";

        [Fact]
        public async Task Register_CreatesAnEmployee_AndReturnsAToken()
        {
            var client = _factory.CreateClient();
            var email = NewEmail();

            var response = await client.PostAsJsonAsync("/api/auth/register",
                new { email, password = ApiFactory.StandardPassword }).ExpectAsync(HttpStatusCode.OK);
            var json = await response.ReadJsonAsync();

            Assert.Equal(email, json.Str("email"));
            Assert.Equal(new[] { RoleNames.Employee }, json.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            Assert.False(string.IsNullOrWhiteSpace(json.Str("token")));
        }

        [Fact]
        public async Task Register_IgnoresAnyRoleTheClientTriesToSend()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/register",
                new { email = NewEmail(), password = ApiFactory.StandardPassword, role = "Admin", roles = new[] { "Admin" } })
                .ExpectAsync(HttpStatusCode.OK);

            var roles = (await response.ReadJsonAsync()).GetProperty("roles").EnumerateArray().Select(r => r.GetString());
            Assert.Equal(new[] { RoleNames.Employee }, roles);
        }

        [Theory]
        [InlineData("not-an-email", "Test#Pass123")]
        [InlineData("a@example.com", "short")]
        [InlineData("a@example.com", "alllowercase1!")]
        [InlineData("a@example.com", "NoDigitsHere!")]
        [InlineData("", "Test#Pass123")]
        public async Task Register_RejectsInvalidInput(string email, string password)
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Register_RejectsAnEmailThatIsAlreadyTaken_EvenInDifferentCapitals()
        {
            var client = _factory.CreateClient();
            var email = NewEmail();
            await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.StandardPassword })
                .ExpectAsync(HttpStatusCode.OK);

            await client.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = ApiFactory.StandardPassword })
                .ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Login_WithTheRightPassword_ReturnsATokenWithTheUsersRoles()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email = manager.Email, password = manager.Password }).ExpectAsync(HttpStatusCode.OK);
            var json = await response.ReadJsonAsync();

            Assert.Equal(new[] { RoleNames.Manager }, json.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        }

        [Fact]
        public async Task Login_AWrongPasswordAndAnUnknownEmailGetTheSameAnswer()
        {
            var user = await _factory.CreateUserAsync();
            var client = _factory.CreateClient();

            var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
                new { email = user.Email, password = "Wrong#Pass999" }).ExpectAsync(HttpStatusCode.Unauthorized);
            var unknownEmail = await client.PostAsJsonAsync("/api/auth/login",
                new { email = NewEmail(), password = "Wrong#Pass999" }).ExpectAsync(HttpStatusCode.Unauthorized);

            // Nothing tells an attacker whether the email exists.
            Assert.Equal((await wrongPassword.ReadJsonAsync()).Str("message"), (await unknownEmail.ReadJsonAsync()).Str("message"));
        }

        [Fact]
        public async Task Login_LocksTheAccountAfterRepeatedWrongPasswords()
        {
            var user = await _factory.CreateUserAsync();
            var client = _factory.CreateClient();

            for (var attempt = 0; attempt < 5; attempt++)
            {
                await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Wrong#Pass999" })
                    .ExpectAsync(HttpStatusCode.Unauthorized);
            }

            // Even the right password is refused while the account is locked.
            await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password })
                .ExpectAsync(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ProtectedEndpoints_RejectMissingTamperedAndGarbageTokens()
        {
            var user = await _factory.CreateUserAsync();
            var token = await ApiHelpers.LoginAndGetTokenAsync(_factory.CreateClient(), user.Email, user.Password);
            var tampered = token[..^3] + (token.EndsWith("abc") ? "xyz" : "abc");

            var client = _factory.CreateClient();
            await client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "abc.def.ghi");
            await client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
            await client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);
        }
    }
}
