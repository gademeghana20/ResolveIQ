using System.Net;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// The Angular app runs on a different origin, so the browser only lets it read API replies
    /// if the API says that origin is allowed. These tests check who is allowed.
    /// </summary>
    [Collection(ApiCollection.Name)]
    public class CorsTests
    {
        private const string AngularOrigin = "http://localhost:4200";

        private readonly ApiFactory _factory;

        public CorsTests(ApiFactory factory) => _factory = factory;

        // The browser's "preflight": a question it asks before the real request.
        private static HttpRequestMessage Preflight(string origin, string method = "PUT") =>
            new(HttpMethod.Options, "/api/incidents")
            {
                Headers =
                {
                    { "Origin", origin },
                    { "Access-Control-Request-Method", method },
                    { "Access-Control-Request-Headers", "authorization,content-type" }
                }
            };

        private static string? AllowedOriginIn(HttpResponseMessage response) =>
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

        [Fact]
        public async Task TheAngularAppIsAllowed_ToAskAboutAnyMethodAndSendAToken()
        {
            var response = await _factory.CreateClient().SendAsync(Preflight(AngularOrigin));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(AngularOrigin, AllowedOriginIn(response));
            Assert.Contains("PUT", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
            Assert.Contains("authorization", response.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant());
        }

        [Fact]
        public async Task AnyOtherWebsiteIsNotAllowed()
        {
            var response = await _factory.CreateClient().SendAsync(Preflight("https://evil.example.com"));

            Assert.Null(AllowedOriginIn(response));
        }

        [Fact]
        public async Task AnAllowedOriginCanReadARealReply_EvenAnErrorOne()
        {
            // A logged-out call gets 401, and Angular must still be able to read that 401
            // (otherwise it would only see a vague "network error").
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/incidents");
            request.Headers.Add("Origin", AngularOrigin);

            var response = await _factory.CreateClient().SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(AngularOrigin, AllowedOriginIn(response));
        }
    }
}
