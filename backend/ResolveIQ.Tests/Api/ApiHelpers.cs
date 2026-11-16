using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Constants;
using Xunit.Sdk;

namespace ResolveIQ.Tests.Api
{
    /// <summary>A user created for one test, with an HTTP client that is already logged in as them.</summary>
    public class TestUser
    {
        private readonly ApiFactory _factory;

        public TestUser(ApiFactory factory, string id, string email, string role)
        {
            _factory = factory;
            Id = id;
            Email = email;
            Role = role;
            Client = factory.CreateClient();
        }

        public string Id { get; }

        public string Email { get; }

        public string Role { get; }

        public string Password => ApiFactory.StandardPassword;

        public HttpClient Client { get; }

        /// <summary>Logs in (again) and starts sending the new token. Needed after a role change or deactivation.</summary>
        public async Task LoginAsync()
        {
            var token = await ApiHelpers.LoginAndGetTokenAsync(_factory.CreateClient(), Email, Password);
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public static class ApiHelpers
    {
        /// <summary>Creates a brand-new user with the given role straight in the database, and logs them in.</summary>
        public static async Task<TestUser> CreateUserAsync(this ApiFactory factory, string role = RoleNames.Employee)
        {
            using var scope = factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com";
            var user = new IdentityUser { UserName = email, Email = email };

            var created = await userManager.CreateAsync(user, ApiFactory.StandardPassword);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(user, role);

            var testUser = new TestUser(factory, user.Id, email, role);
            await testUser.LoginAsync();
            return testUser;
        }

        public static async Task<string> LoginAndGetTokenAsync(HttpClient client, string email, string password)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
            await response.ExpectAsync(HttpStatusCode.OK);
            return (await response.ReadJsonAsync()).Str("token");
        }

        // ---- reading responses ----

        public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        public static string Str(this JsonElement element, string property) =>
            element.GetProperty(property).GetString()!;

        public static string? StrOrNull(this JsonElement element, string property) =>
            element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetString();

        public static Guid GetGuid(this JsonElement element, string property) =>
            System.Guid.Parse(element.GetProperty(property).GetString()!);

        public static DateTime Time(this JsonElement element, string property) =>
            element.GetProperty(property).GetDateTime().ToUniversalTime();

        public static DateTime? TimeOrNull(this JsonElement element, string property) =>
            element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.Time(property);

        // ---- assertions that show the response body when they fail ----

        public static async Task<HttpResponseMessage> ExpectAsync(this HttpResponseMessage response, HttpStatusCode expected)
        {
            if (response.StatusCode != expected)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new XunitException(
                    $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode} " +
                    $"for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}. Body: {body}");
            }

            return response;
        }

        public static async Task<HttpResponseMessage> ExpectAsync(this Task<HttpResponseMessage> call, HttpStatusCode expected) =>
            await (await call).ExpectAsync(expected);

        // ---- short-hand for common actions ----

        public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
            client.PutAsJsonAsync(url, body);

        /// <summary>Reports a new incident and returns its JSON.</summary>
        public static async Task<JsonElement> ReportIncidentAsync(
            this TestUser reporter,
            string title = "Printer is broken",
            string description = "Paper jam on floor 2",
            Guid? categoryId = null)
        {
            var response = await reporter.Client.PostAsJsonAsync("/api/incidents", new { title, description, categoryId });
            await response.ExpectAsync(HttpStatusCode.Created);
            return await response.ReadJsonAsync();
        }

        /// <summary>A manager assigns (triages) an incident, setting its priority. Returns the updated incident.</summary>
        public static async Task<JsonElement> AssignAsync(
            this TestUser manager,
            JsonElement incident,
            TestUser assignee,
            string? priority = "High")
        {
            var response = await manager.Client.PutAsJsonAsync(
                $"/api/incidents/{incident.GetGuid("id")}/assign",
                new { assigneeUserId = assignee.Id, priority });
            await response.ExpectAsync(HttpStatusCode.OK);
            return await response.ReadJsonAsync();
        }

        public static Task<HttpResponseMessage> SetStatusAsync(
            this TestUser who,
            JsonElement incident,
            string status,
            string? reason = null) =>
            who.Client.PutAsJsonAsync($"/api/incidents/{incident.GetGuid("id")}/status", new { status, reason });

        public static async Task<JsonElement> GetIncidentAsync(this TestUser who, JsonElement incident)
        {
            var response = await who.Client.GetAsync($"/api/incidents/{incident.GetGuid("id")}");
            await response.ExpectAsync(HttpStatusCode.OK);
            return await response.ReadJsonAsync();
        }

        public static async Task<JsonElement> ListIncidentsAsync(this TestUser who, string query = "")
        {
            var response = await who.Client.GetAsync("/api/incidents" + (query.Length == 0 ? "" : "?" + query));
            await response.ExpectAsync(HttpStatusCode.OK);
            return await response.ReadJsonAsync();
        }
    }
}
