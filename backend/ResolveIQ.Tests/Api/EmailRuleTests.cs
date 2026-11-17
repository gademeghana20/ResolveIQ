using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// A new account needs a full email address (name@example.com). Older accounts that were allowed
    /// before this rule must still be able to sign in.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class EmailRuleTests
    {
        private readonly ApiFactory _factory;

        public EmailRuleTests(ApiFactory factory) => _factory = factory;

        private static string Unique(string domain) => $"rule.{Guid.NewGuid():N}@{domain}";

        private Task<HttpResponseMessage> RegisterAsync(string email) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.StandardPassword });

        [Theory]
        [InlineData("example")]
        [InlineData("localhost")]
        [InlineData("gmail")]
        [InlineData("example.")]
        [InlineData(".com")]
        [InlineData("example..com")]
        [InlineData("example.c")]
        public async Task RegisteringWithoutAFullDomain_IsRefused_AndNoAccountIsMade(string domain)
        {
            var email = Unique(domain);

            var response = await RegisterAsync(email).ExpectAsync(HttpStatusCode.BadRequest);

            var errors = (await response.ReadJsonAsync()).GetProperty("errors");
            Assert.True(errors.TryGetProperty("Email", out var messages));
            Assert.Contains("name@example.com", messages[0].GetString());
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.StandardPassword })
                .ExpectAsync(HttpStatusCode.Unauthorized); // nothing was created
        }

        [Theory]
        [InlineData("example.com")]
        [InlineData("mail.example.com")]
        [InlineData("example.co.uk")]
        public async Task RegisteringWithAFullAddress_Works(string domain)
        {
            await RegisterAsync(Unique(domain)).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AnAdminCannotCreateAUserWithoutAFullDomainEither()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            var response = await admin.Client.PostAsJsonAsync(
                "/api/users",
                new { email = Unique("example"), password = ApiFactory.StandardPassword, role = "Employee" })
                .ExpectAsync(HttpStatusCode.BadRequest);

            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("Email", out _));

            await admin.Client.PostAsJsonAsync(
                "/api/users",
                new { email = Unique("example.com"), password = ApiFactory.StandardPassword, role = "Employee" })
                .ExpectAsync(HttpStatusCode.Created);
        }

        [Fact]
        public async Task AnOlderAccountWithoutAFullDomain_CanStillSignIn()
        {
            // Created straight in the database, the way accounts like "test1@example" came to exist.
            var email = Unique("example");
            using (var scope = _factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var user = new IdentityUser { UserName = email, Email = email };
                Assert.True((await users.CreateAsync(user, ApiFactory.StandardPassword)).Succeeded);
                await users.AddToRoleAsync(user, RoleNames.Employee);
            }

            var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.StandardPassword })
                .ExpectAsync(HttpStatusCode.OK);

            Assert.Equal(email, (await login.ReadJsonAsync()).Str("email"));
        }

        [Fact]
        public async Task SigningInStillRefusesSomethingThatIsNotAnEmailAtAll()
        {
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "x" })
                .ExpectAsync(HttpStatusCode.BadRequest);
        }
    }
}
