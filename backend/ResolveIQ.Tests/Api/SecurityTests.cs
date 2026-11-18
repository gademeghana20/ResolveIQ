using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// Every endpoint, tried by every kind of caller. Who may call what is decided on the server,
    /// so this is the test that would catch a forgotten [Authorize].
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class PermissionMatrixTests
    {
        private static readonly string[] Everyone = { RoleNames.Employee, RoleNames.Manager, RoleNames.Admin };
        private static readonly string[] ManagersAndAdmins = { RoleNames.Manager, RoleNames.Admin };
        private static readonly string[] AdminsOnly = { RoleNames.Admin };

        private static readonly string SomeId = Guid.NewGuid().ToString();

        // Method, path, and the roles allowed to call it. Allowed callers are sent an empty body {}, so the
        // request fails validation (400) or finds nothing (404) and changes nothing, but is not turned away.
        private static readonly (string Method, string Path, string[] Allowed)[] Endpoints =
        {
            ("GET", "/api/incidents", Everyone),
            ("GET", $"/api/incidents/{SomeId}", Everyone),
            ("POST", "/api/incidents", Everyone),
            ("PUT", $"/api/incidents/{SomeId}", Everyone),
            ("PUT", $"/api/incidents/{SomeId}/assign", ManagersAndAdmins),
            ("PUT", $"/api/incidents/{SomeId}/status", Everyone),
            ("PUT", $"/api/incidents/{SomeId}/priority", ManagersAndAdmins),
            ("GET", $"/api/incidents/{SomeId}/history", Everyone),
            ("GET", $"/api/incidents/{SomeId}/comments", Everyone),
            ("POST", $"/api/incidents/{SomeId}/comments", Everyone),
            ("GET", "/api/notifications", Everyone),
            ("PUT", $"/api/notifications/{SomeId}/read", Everyone),
            ("PUT", "/api/notifications/read-all", Everyone),
            ("GET", "/api/dashboard/summary", Everyone),
            ("GET", "/api/dashboard/incident-statistics", Everyone),
            ("GET", "/api/users", ManagersAndAdmins),
            ("GET", "/api/users/some-user", ManagersAndAdmins),
            ("POST", "/api/users", AdminsOnly),
            ("PUT", "/api/users/some-user/role", AdminsOnly),
            ("PUT", "/api/users/some-user/active", AdminsOnly),
            ("POST", "/api/users/some-user/password-reset-email", AdminsOnly),
            ("GET", "/api/sla/policies", ManagersAndAdmins),
            ("PUT", "/api/sla/policies/Low", AdminsOnly),
            ("GET", "/api/categories", Everyone),
            ("POST", "/api/categories", AdminsOnly),
            ("PUT", $"/api/categories/{SomeId}", AdminsOnly)
        };

        public static IEnumerable<object[]> EndpointCases() =>
            Endpoints.Select(e => new object[] { $"{e.Method} {e.Path.Replace(SomeId, "{id}")}" });

        private readonly ApiFactory _factory;

        public PermissionMatrixTests(ApiFactory factory) => _factory = factory;

        private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json")
        };

        [Theory]
        [MemberData(nameof(EndpointCases))]
        public async Task EachRoleIsLetInOrTurnedAwayCorrectly(string label)
        {
            var endpoint = Endpoints.Single(e => $"{e.Method} {e.Path.Replace(SomeId, "{id}")}" == label);

            // Nobody logged in: always 401.
            var anonymous = await _factory.CreateClient().SendAsync(Request(endpoint.Method, endpoint.Path));
            Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized,
                $"{label} without a login gave {(int)anonymous.StatusCode}, expected 401");

            foreach (var role in Everyone)
            {
                var user = await _factory.CreateUserAsync(role);
                var response = await user.Client.SendAsync(Request(endpoint.Method, endpoint.Path));

                if (endpoint.Allowed.Contains(role))
                {
                    Assert.True(
                        response.StatusCode != HttpStatusCode.Unauthorized && response.StatusCode != HttpStatusCode.Forbidden,
                        $"{label} should let a {role} in, but gave {(int)response.StatusCode}");
                }
                else
                {
                    Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                        $"{label} should refuse a {role} with 403, but gave {(int)response.StatusCode}");
                }
            }
        }

        [Fact]
        public async Task TheMatrixCoversEveryEndpointTheApiHas()
        {
            // If someone adds a controller action and forgets to add it to the matrix above, this fails.
            var apiEndpoints = _factory.Services
                .GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
                .Endpoints
                .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText?.StartsWith("api/") == true
                            && !e.RoutePattern.RawText.StartsWith("api/auth/"))   // open to anyone by design; see AuthTests
                .Select(e => e.RoutePattern.RawText!)
                .Distinct()
                .ToList();

            // Guard against passing by accident: the API has 17 distinct routes outside /api/auth
            // (several HTTP methods can share one route, so there are more actions than routes).
            Assert.True(apiEndpoints.Count >= 17, $"Expected to discover the API's routes, found only {apiEndpoints.Count}.");

            // Compare by shape: replace every {parameter} and concrete id with a placeholder.
            static string Shape(string route) =>
                System.Text.RegularExpressions.Regex.Replace(
                    System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", "{}"),
                    @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|some-user|/Low$|/Low/",
                    m => m.Value.StartsWith("/") ? m.Value.Replace("Low", "{}") : "{}");

            var matrixShapes = Endpoints.Select(e => Shape(e.Path.TrimStart('/'))).Distinct().ToHashSet();
            var missing = apiEndpoints.Select(Shape).Distinct().Where(s => !matrixShapes.Contains(s)).ToList();

            Assert.True(missing.Count == 0, "Endpoints missing from the permission matrix: " + string.Join(", ", missing));
        }

        [Fact]
        public async Task AuthEndpointsAreOpenToAnyone_AndNothingElseIs()
        {
            var anonymous = _factory.CreateClient();

            // These must work without a login. A 400 (bad body) is fine: the point is it is not 401.
            var register = await anonymous.PostAsJsonAsync("/api/auth/register", new { });
            var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { });

            Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        }
    }

    [Collection(ApiCollection.Name)]
    public class ErrorFormatTests
    {
        private readonly ApiFactory _factory;

        public ErrorFormatTests(ApiFactory factory) => _factory = factory;

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            await response.ExpectAsync(expected);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal((int)expected, (await response.ReadJsonAsync()).GetProperty("status").GetInt32());
            Assert.DoesNotContain("   at ", body);        // never a stack trace
            Assert.DoesNotContain("Exception", body);
        }

        [Fact]
        public async Task EveryKindOfErrorUsesTheSameProblemFormat()
        {
            var employee = await _factory.CreateUserAsync();

            await AssertProblemAsync(await _factory.CreateClient().GetAsync("/api/incidents"), HttpStatusCode.Unauthorized);
            await AssertProblemAsync(await employee.Client.GetAsync("/api/users"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await employee.Client.GetAsync($"/api/incidents/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
            await AssertProblemAsync(await employee.Client.GetAsync("/api/no-such-endpoint"), HttpStatusCode.NotFound);
            await AssertProblemAsync(await employee.Client.PostAsJsonAsync("/api/incidents", new { title = "" }), HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task ValidationErrorsSayWhichFieldIsWrong()
        {
            var employee = await _factory.CreateUserAsync();

            var response = await employee.Client.PostAsJsonAsync("/api/incidents", new { title = "", description = "" });
            var errors = (await response.ReadJsonAsync()).GetProperty("errors");

            Assert.True(errors.TryGetProperty("Title", out _));
            Assert.True(errors.TryGetProperty("Description", out _));
        }

        [Fact]
        public async Task AMalformedBodyIsABadRequest_NotACrash()
        {
            var employee = await _factory.CreateUserAsync();

            var response = await employee.Client.PostAsync("/api/incidents",
                new StringContent("{ this is not json", Encoding.UTF8, "application/json"));

            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task AWrongContentTypeIsRejectedCleanly()
        {
            var employee = await _factory.CreateUserAsync();

            var response = await employee.Client.PostAsync("/api/incidents", new StringContent("title=x", Encoding.UTF8, "text/plain"));

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }

    [Collection(ApiCollection.Name)]
    public class ConcurrencyTests
    {
        private readonly ApiFactory _factory;

        public ConcurrencyTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task TwoPeopleEditingTheSameIncident_TheSecondSaveIsRefused_NotSilentlyOverwriting()
        {
            var s = await Scenario.AssignedAsync(_factory);

            // Two separate "sessions" read the same incident.
            using var scopeA = _factory.Services.CreateScope();
            using var scopeB = _factory.Services.CreateScope();
            var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var seenByA = await dbA.Incidents.SingleAsync(i => i.Id == s.Id);
            var seenByB = await dbB.Incidents.SingleAsync(i => i.Id == s.Id);
            Assert.Equal(seenByA.RowVersion, seenByB.RowVersion);

            // A saves first.
            seenByA.Title = "Written by A";
            await dbA.SaveChangesAsync();

            // B is now working from stale data, so the database must refuse.
            seenByB.Title = "Written by B";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

            // A's change survived.
            using var scopeC = _factory.Services.CreateScope();
            var final = await scopeC.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Incidents.AsNoTracking().SingleAsync(i => i.Id == s.Id);
            Assert.Equal("Written by A", final.Title);
        }

        [Fact]
        public async Task EveryChangeGivesTheIncidentANewVersion()
        {
            var s = await Scenario.AssignedAsync(_factory);

            async Task<byte[]> VersionAsync()
            {
                using var scope = _factory.Services.CreateScope();
                var incident = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .Incidents.AsNoTracking().SingleAsync(i => i.Id == s.Id);
                return incident.RowVersion;
            }

            var before = await VersionAsync();
            await s.Assignee.SetStatusAsync(s.Incident, "InProgress").ExpectAsync(HttpStatusCode.OK);
            var after = await VersionAsync();

            Assert.NotEqual(before, after);
        }
    }
}
