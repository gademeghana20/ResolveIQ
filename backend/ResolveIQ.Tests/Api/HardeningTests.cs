using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.API.Health;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Data;
using ResolveIQ.Infrastructure.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResolveIQ.Application.Abstractions;

namespace ResolveIQ.Tests.Api
{
    // ---- the app with a low sign-in limit ----

    public class RateLimitedApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["RateLimiting:AuthPermitLimit"] = "3",
            ["RateLimiting:GeneralPermitLimit"] = "12"
        };
    }

    // ---- the app with registration closed, and one limited to a company's email addresses ----

    public class ClosedRegistrationApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Registration:Enabled"] = "false"
        };
    }

    public class DomainLimitedApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Registration:AllowedEmailDomains:0"] = "corp.test",
            ["Registration:AllowedEmailDomains:1"] = "partner.test"
        };
    }

    /// <summary>The browser-safety headers every answer carries.</summary>
    [Collection(ApiCollection.Name)]
    public class SecurityHeaderTests
    {
        private readonly ApiFactory _factory;

        public SecurityHeaderTests(ApiFactory factory) => _factory = factory;

        private static string Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.Single() : string.Empty;

        private static void AssertSafe(HttpResponseMessage response)
        {
            Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
            Assert.Equal("DENY", Header(response, "X-Frame-Options"));
            Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
            Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
            Assert.Contains("default-src 'none'", Header(response, "Content-Security-Policy"));
            Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
            Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
        }

        [Fact]
        public async Task ASuccessfulAnswerCarriesThem()
        {
            var person = await _factory.CreateUserAsync();

            AssertSafe(await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK));
        }

        [Fact]
        public async Task SoDoARefusal_ANotFound_AndABadRequest()
        {
            AssertSafe(await _factory.CreateClient().GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized));
            AssertSafe(await _factory.CreateClient().GetAsync("/api/no-such-thing").ExpectAsync(HttpStatusCode.NotFound));
            AssertSafe(await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "x", password = "y" })
                .ExpectAsync(HttpStatusCode.BadRequest));
        }

        [Fact]
        public async Task SignInAnswersAreNeverCached()
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email = "nobody@example.com", password = "Whatever#123" });

            Assert.Equal("no-store", Header(response, "Cache-Control"));
        }

        [Fact]
        public async Task TheServerDoesNotAnnounceWhatSoftwareItIs()
        {
            var response = await _factory.CreateClient().GetAsync("/health");

            Assert.False(response.Headers.Contains("Server"));
        }
    }

    /// <summary>"Is the app up?" for hosting services.</summary>
    [Collection(ApiCollection.Name)]
    public class HealthTests
    {
        private readonly ApiFactory _factory;

        public HealthTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task AnyoneCanAsk_AndItSaysHealthy()
        {
            var response = await _factory.CreateClient().GetAsync("/health").ExpectAsync(HttpStatusCode.OK);

            Assert.Equal("Healthy", (await response.ReadJsonAsync()).Str("status"));
        }

        [Fact]
        public async Task TheAnswerIsOnlyTheStatus_NothingAboutTheServerOrDatabase()
        {
            var body = await (await _factory.CreateClient().GetAsync("/health")).Content.ReadAsStringAsync();

            Assert.Equal("{\"status\":\"Healthy\"}", body);
        }

        [Fact]
        public async Task ItIsUnhealthy_WhenTheDatabaseCannotBeReached()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=127.0.0.1,1;Database=nothing;User Id=sa;Password=x;Connect Timeout=1;TrustServerCertificate=True")
                .Options;
            await using var unreachable = new ApplicationDbContext(options);

            var result = await new DatabaseHealthCheck(unreachable).CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Null(result.Exception);                       // no error details are passed on
            Assert.True(string.IsNullOrEmpty(result.Description));
        }
    }

    /// <summary>One address cannot flood the sign-in endpoints.</summary>
    public class RateLimitTests : IClassFixture<RateLimitedApiFactory>
    {
        private readonly RateLimitedApiFactory _factory;

        public RateLimitTests(RateLimitedApiFactory factory) => _factory = factory;

        [Fact]
        public async Task AfterTheLimit_SignInCallsAreTurnedAway_WithHowLongToWait()
        {
            var client = _factory.CreateClient();
            var body = new { email = "someone@example.com", password = "Wrong#Pass123" };

            for (var i = 0; i < 3; i++)
            {
                await client.PostAsJsonAsync("/api/auth/login", body).ExpectAsync(HttpStatusCode.Unauthorized);
            }

            var refused = await client.PostAsJsonAsync("/api/auth/login", body).ExpectAsync(HttpStatusCode.TooManyRequests);

            Assert.Equal("60", refused.Headers.GetValues("Retry-After").Single());
            Assert.Equal("Too many requests", (await refused.ReadJsonAsync()).Str("title"));
            Assert.Equal("nosniff", refused.Headers.GetValues("X-Content-Type-Options").Single()); // still carries the safety headers
        }

        [Fact]
        public async Task ForgotPasswordAndRegisterShareTheSameLimit_SoNeitherCanBeUsedToFloodEmail()
        {
            var factory = new RateLimitedApiFactory();
            await ((IAsyncLifetime)factory).InitializeAsync();
            try
            {
                var client = factory.CreateClient();
                for (var i = 0; i < 3; i++)
                {
                    await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = $"a{i}@example.com" })
                        .ExpectAsync(HttpStatusCode.OK);
                }

                await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "b@example.com" })
                    .ExpectAsync(HttpStatusCode.TooManyRequests);
                await client.PostAsJsonAsync("/api/auth/register", new { email = "c@example.com", password = "Str0ng#Pass1" })
                    .ExpectAsync(HttpStatusCode.TooManyRequests);
            }
            finally
            {
                await ((IAsyncLifetime)factory).DisposeAsync();
            }
        }

        [Fact]
        public async Task TheHealthCheck_IsNeverRateLimited()
        {
            var client = _factory.CreateClient();

            for (var i = 0; i < 30; i++)
            {
                await client.GetAsync("/health").ExpectAsync(HttpStatusCode.OK);
            }
        }
    }

    /// <summary>Who may create their own account.</summary>
    public class RegistrationOpenTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;

        public RegistrationOpenTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task ByDefaultRegistrationIsOpen_ToAnyDomain()
        {
            var status = await (await _factory.CreateClient().GetAsync("/api/auth/registration").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();

            Assert.True(status.GetProperty("enabled").GetBoolean());
            Assert.Equal(0, status.GetProperty("allowedEmailDomains").GetArrayLength());
        }
    }

    public class RegistrationClosedTests : IClassFixture<ClosedRegistrationApiFactory>
    {
        private readonly ClosedRegistrationApiFactory _factory;

        public RegistrationClosedTests(ClosedRegistrationApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TheStatusSaysSo()
        {
            var status = await (await _factory.CreateClient().GetAsync("/api/auth/registration")).ReadJsonAsync();

            Assert.False(status.GetProperty("enabled").GetBoolean());
        }

        [Fact]
        public async Task NobodyCanRegister_ButAnAdminCanStillAddPeople_AndExistingPeopleSignInAsUsual()
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
                new { email = $"new.{Guid.NewGuid():N}@example.com", password = ApiFactory.StandardPassword })
                .ExpectAsync(HttpStatusCode.Forbidden);
            Assert.Equal("Registration is closed", (await response.ReadJsonAsync()).Str("title"));

            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            await admin.Client.PostAsJsonAsync("/api/users", new { email = $"added.{Guid.NewGuid():N}@example.com", role = "Employee" })
                .ExpectAsync(HttpStatusCode.Created);

            var existing = await _factory.CreateUserAsync();
            await existing.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);
        }
    }

    public class RegistrationDomainTests : IClassFixture<DomainLimitedApiFactory>
    {
        private readonly DomainLimitedApiFactory _factory;

        public RegistrationDomainTests(DomainLimitedApiFactory factory) => _factory = factory;

        private Task<HttpResponseMessage> RegisterAsync(string email) =>
            _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.StandardPassword });

        [Fact]
        public async Task TheStatusListsTheAllowedDomains()
        {
            var status = await (await _factory.CreateClient().GetAsync("/api/auth/registration")).ReadJsonAsync();

            Assert.Equal(
                new[] { "corp.test", "partner.test" },
                status.GetProperty("allowedEmailDomains").EnumerateArray().Select(d => d.GetString()));
        }

        [Theory]
        [InlineData("dana@corp.test")]
        [InlineData("lee@PARTNER.test")]       // capital letters do not matter
        public async Task AnAllowedDomainCanRegister(string address)
        {
            await RegisterAsync($"{Guid.NewGuid():N}.{address}").ExpectAsync(HttpStatusCode.OK);
        }

        [Theory]
        [InlineData("dana@other.test")]
        [InlineData("dana@sub.corp.test")]          // a sub-domain is a different domain
        [InlineData("dana@corp.test.evil.com")]     // and so is one that merely starts with it
        public async Task AnyOtherDomainIsRefused_WithTheListOfAllowedOnes(string address)
        {
            var response = await RegisterAsync(address).ExpectAsync(HttpStatusCode.BadRequest);

            var message = (await response.ReadJsonAsync()).GetProperty("errors").GetProperty("Email")[0].GetString();
            Assert.Contains("corp.test, partner.test", message);
        }

        [Fact]
        public async Task TheLimitDoesNotAffectAnAdminAddingSomeone()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await admin.Client.PostAsJsonAsync("/api/users", new { email = $"added.{Guid.NewGuid():N}@other.test", role = "Employee" })
                .ExpectAsync(HttpStatusCode.Created);
        }
    }

    /// <summary>The housekeeping jobs, run directly.</summary>
    [Collection(ApiCollection.Name)]
    public class MaintenanceTests
    {
        private readonly ApiFactory _factory;

        public MaintenanceTests(ApiFactory factory) => _factory = factory;

        private async Task<int> RunCleanupAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MaintenanceJobs>().DeleteExpiredRefreshTokensAsync();
        }

        private async Task<int> CountTokensAsync(string userId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.RefreshTokens.CountAsync(t => t.UserId == userId);
        }

        [Fact]
        public async Task ExpiredRefreshTokensAreRemoved_ButOnesStillValidAreKept()
        {
            var person = await _factory.CreateUserAsync();
            using (var scope = _factory.Services.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
                var stamp = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>()
                    .FindByIdAsync(person.Id))!.SecurityStamp!;
                await tokens.IssueAsync(person.Id, stamp);
                await tokens.IssueAsync(person.Id, stamp);
            }

            var before = await CountTokensAsync(person.Id);
            _factory.Time.Advance(TimeSpan.FromDays(8));                  // all of them are now past their 7 days
            using (var scope = _factory.Services.CreateScope())
            {
                var stamp = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>()
                    .FindByIdAsync(person.Id))!.SecurityStamp!;
                await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>().IssueAsync(person.Id, stamp); // a fresh one
            }

            await RunCleanupAsync();

            Assert.True(before >= 3);                                      // the login plus the two issued
            Assert.Equal(1, await CountTokensAsync(person.Id));            // only the fresh one is left
        }

        [Fact]
        public async Task WithNothingToRemove_ItRemovesNothing()
        {
            await RunCleanupAsync();                                       // clear anything left by other tests
            Assert.Equal(0, await RunCleanupAsync());
        }
    }
}
