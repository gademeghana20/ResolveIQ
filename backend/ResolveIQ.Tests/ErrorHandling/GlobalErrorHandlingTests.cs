using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResolveIQ.API.ErrorHandling;

namespace ResolveIQ.Tests.ErrorHandling
{
    public class GlobalErrorHandlingTests
    {
        private const string SecretDetail = "secret internal detail: server=db01;password=hunter2";

        // Starts a tiny real web server in memory, wired up exactly like the API's error handling.
        private static async Task<WebApplication> StartAppAsync(string environment)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddGlobalErrorHandling();

            var app = builder.Build();
            app.UseGlobalErrorHandling();

            app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException(SecretDetail)));
            app.MapGet("/forbidden", () => Results.StatusCode(StatusCodes.Status403Forbidden));
            app.MapGet("/fine", () => "all good");

            await app.StartAsync();
            return app;
        }

        [Fact]
        public async Task ACrash_ReturnsACleanProblemResponse_NotAStackTrace()
        {
            await using var app = await StartAppAsync(Environments.Production);
            var response = await app.GetTestClient().GetAsync("/boom");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            using var json = JsonDocument.Parse(body);
            Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("title").GetString());
            Assert.True(json.RootElement.TryGetProperty("traceId", out _), "a traceId lets support find the log entry");

            Assert.DoesNotContain("InvalidOperationException", body);
            Assert.DoesNotContain("   at ", body);
        }

        [Fact]
        public async Task InProduction_TheInternalMessageIsNeverSentToTheClient()
        {
            await using var app = await StartAppAsync(Environments.Production);
            var body = await (await app.GetTestClient().GetAsync("/boom")).Content.ReadAsStringAsync();

            Assert.DoesNotContain("hunter2", body);
            Assert.DoesNotContain("secret internal detail", body);
            Assert.False(JsonDocument.Parse(body).RootElement.TryGetProperty("detail", out _));
        }

        [Fact]
        public async Task InDevelopment_TheMessageIsShown_ButStillNoStackTrace()
        {
            await using var app = await StartAppAsync(Environments.Development);
            var body = await (await app.GetTestClient().GetAsync("/boom")).Content.ReadAsStringAsync();

            Assert.Contains("secret internal detail", body);
            Assert.DoesNotContain("   at ", body);
        }

        [Fact]
        public async Task EmptyErrorResponses_AlsoUseTheSameFormat()
        {
            await using var app = await StartAppAsync(Environments.Production);
            var client = app.GetTestClient();

            var notFound = await client.GetAsync("/no-such-page");
            var forbidden = await client.GetAsync("/forbidden");

            Assert.Equal(404, (int)notFound.StatusCode);
            Assert.Equal("application/problem+json", notFound.Content.Headers.ContentType?.MediaType);
            Assert.Equal(404, JsonDocument.Parse(await notFound.Content.ReadAsStringAsync())
                .RootElement.GetProperty("status").GetInt32());

            Assert.Equal(403, (int)forbidden.StatusCode);
            Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task NormalRequests_AreUntouched()
        {
            await using var app = await StartAppAsync(Environments.Production);
            var response = await app.GetTestClient().GetAsync("/fine");

            Assert.Equal(200, (int)response.StatusCode);
            Assert.Equal("all good", await response.Content.ReadAsStringAsync());
        }
    }
}
