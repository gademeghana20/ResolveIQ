using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Tests.Api
{
    /// <summary>The app was started with the first-Admin settings, like a real deployment would be.</summary>
    [Collection(ApiCollection.Name)]
    public class BootstrapAdminStartupTests
    {
        private readonly ApiFactory _factory;

        public BootstrapAdminStartupTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TheFirstAdminExistsFromTheStartupSettings_AndCanLogInAsAdmin()
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email = ApiFactory.BootstrapAdminEmail, password = ApiFactory.BootstrapAdminPassword })
                .ExpectAsync(HttpStatusCode.OK);

            var roles = (await response.ReadJsonAsync()).GetProperty("roles").EnumerateArray().Select(r => r.GetString());
            Assert.Equal(new[] { RoleNames.Admin }, roles);
        }

        [Fact]
        public async Task OnceAnAdminExists_FurtherAttemptsDoNothing()
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            var outcome = await IdentitySeeder.EnsureFirstAdminAsync(
                userManager, "second.admin@example.com", ApiFactory.StandardPassword);

            Assert.Equal(BootstrapAdminResult.AdminAlreadyExists, outcome);
            Assert.Null(await userManager.FindByEmailAsync("second.admin@example.com"));
        }
    }

    /// <summary>The app was started WITHOUT first-Admin settings, so these tests control the bootstrap themselves.</summary>
    public class BootstrapAdminRuleTests : IClassFixture<NoAdminApiFactory>
    {
        private readonly NoAdminApiFactory _factory;

        public BootstrapAdminRuleTests(NoAdminApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TheBootstrapFollowsItsSafetyRules_InOrder()
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // Nothing was configured, so there is no Admin yet.
            Assert.Empty(await userManager.GetUsersInRoleAsync(RoleNames.Admin));

            // A weak password fails loudly, and creates nothing.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IdentitySeeder.EnsureFirstAdminAsync(userManager, "weak.admin@example.com", "abc"));
            Assert.Null(await userManager.FindByEmailAsync("weak.admin@example.com"));

            // If the email already belongs to an ordinary user, that user is NOT promoted.
            var ordinary = new IdentityUser { UserName = "ordinary@example.com", Email = "ordinary@example.com" };
            await userManager.CreateAsync(ordinary, ApiFactory.StandardPassword);
            await userManager.AddToRoleAsync(ordinary, RoleNames.Employee);

            var outcome = await IdentitySeeder.EnsureFirstAdminAsync(
                userManager, "ordinary@example.com", ApiFactory.StandardPassword);

            Assert.Equal(BootstrapAdminResult.EmailAlreadyRegistered, outcome);
            Assert.False(await userManager.IsInRoleAsync(ordinary, RoleNames.Admin));

            // A fresh email creates the first Admin.
            outcome = await IdentitySeeder.EnsureFirstAdminAsync(
                userManager, "owner@example.com", ApiFactory.StandardPassword);

            Assert.Equal(BootstrapAdminResult.Created, outcome);
            var owner = await userManager.FindByEmailAsync("owner@example.com");
            Assert.NotNull(owner);
            Assert.True(await userManager.IsInRoleAsync(owner!, RoleNames.Admin));

            // And now that an Admin exists, the bootstrap stops.
            outcome = await IdentitySeeder.EnsureFirstAdminAsync(
                userManager, "another@example.com", ApiFactory.StandardPassword);

            Assert.Equal(BootstrapAdminResult.AdminAlreadyExists, outcome);
            Assert.Single(await userManager.GetUsersInRoleAsync(RoleNames.Admin));
        }
    }
}
