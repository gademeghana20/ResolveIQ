using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    /// <summary>The app set up like a public demo site: shared demo accounts, made-up incidents, behind a proxy.</summary>
    public class PublicDemoApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Seed:DemoUsers"] = "true",
            ["Seed:DemoPassword"] = StandardPassword,
            ["Seed:DemoData"] = "true",
            ["Demo:ProtectAccounts"] = "true",
            ["Hosting:TrustProxyHeaders"] = "true",
            ["Database:MigrateOnStartup"] = "true"
        };
    }

    /// <summary>Behind a proxy, with a low sign-in limit.</summary>
    public class ProxiedApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Hosting:TrustProxyHeaders"] = "true",
            ["RateLimiting:AuthPermitLimit"] = "3"
        };
    }

    public class BehindAProxyTests : IClassFixture<ProxiedApiFactory>
    {
        private readonly ProxiedApiFactory _factory;

        public BehindAProxyTests(ProxiedApiFactory factory) => _factory = factory;

        [Fact]
        public async Task EachVisitorGetsTheirOwnSignInLimit_NotOneLimitForTheWholeProxy()
        {
            var client = _factory.CreateClient();

            async Task<HttpStatusCode> TryAsync(string fromAddress)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
                {
                    Content = JsonContent.Create(new { email = "nobody@example.com", password = "Wrong#Pass123" })
                };
                request.Headers.Add("X-Forwarded-For", fromAddress);
                return (await client.SendAsync(request)).StatusCode;
            }

            // Limit is 3 a minute per visitor. One visitor is turned away on the fourth try...
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, await TryAsync("203.0.113.10"));
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, await TryAsync("203.0.113.10"));

            // ...while a different visitor is not affected.
            Assert.Equal(HttpStatusCode.Unauthorized, await TryAsync("203.0.113.77"));
        }
    }

    public class PublicDemoTests : IClassFixture<PublicDemoApiFactory>
    {
        private readonly PublicDemoApiFactory _factory;

        public PublicDemoTests(PublicDemoApiFactory factory) => _factory = factory;

        private async Task<HttpClient> SignedInAsync(string email)
        {
            var client = _factory.CreateClient();
            var token = await ApiHelpers.LoginAndGetTokenAsync(client, email, ApiFactory.StandardPassword);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private async Task<string> IdOfAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByEmailAsync(email);
            return user!.Id;
        }

        [Fact]
        public async Task TheDemoAccountsExist_AndTheDemoIncidentsAreThere_EvenOutsideDevelopment()
        {
            var manager = await SignedInAsync("manager@example.com");

            var list = await manager.GetAsync("/api/incidents?pageSize=50").ExpectAsync(HttpStatusCode.OK);

            Assert.Equal(14, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        }

        [Fact]
        public async Task TheSharedDemoAccounts_CannotBeLockedDemotedOrGivenANewPassword()
        {
            var admin = await SignedInAsync("admin@example.com");
            var employeeId = await IdOfAsync("employee@example.com");

            foreach (var response in new[]
                     {
                         await admin.PutAsJsonAsync($"/api/users/{employeeId}/role", new { role = RoleNames.Manager }),
                         await admin.PutAsJsonAsync($"/api/users/{employeeId}/active", new { isActive = false }),
                         await admin.PostAsync($"/api/users/{employeeId}/password-reset-email", null)
                     })
            {
                await response.ExpectAsync(HttpStatusCode.BadRequest);
                Assert.Contains("shared demo account", await response.Content.ReadAsStringAsync());
            }

            // The account itself cannot change its own password either.
            var employee = await SignedInAsync("employee@example.com");
            var change = await employee.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = ApiFactory.StandardPassword, newPassword = "Another#Pass456" })
                .ExpectAsync(HttpStatusCode.BadRequest);
            Assert.Contains("shared demo account", await change.Content.ReadAsStringAsync());

            // "Forgot password" sends nothing for a shared account, and still looks the same from outside.
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email = "employee@example.com" })
                .ExpectAsync(HttpStatusCode.OK);
            Assert.Empty(_factory.Emails.To("employee@example.com"));

            // Still working, after all of that.
            await SignedInAsync("employee@example.com");
        }

        [Fact]
        public async Task OrdinaryAccounts_AreNotProtected()
        {
            var someone = await _factory.CreateUserAsync();
            var admin = await SignedInAsync("admin@example.com");

            await admin.PutAsJsonAsync($"/api/users/{someone.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);
        }
    }
}
