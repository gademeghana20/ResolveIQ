using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace ResolveIQ.API.Security
{
    /// <summary>The "RateLimiting" section of the settings.</summary>
    public class RateLimitSettings
    {
        public const string SectionName = "RateLimiting";

        /// <summary>
        /// How many sign-in style calls (login, register, forgot password, reset, refresh...) one address may make a
        /// minute. Passwords are also protected per account by the lock-out; this stops one address flooding the API
        /// (for example with "forgot password" emails or fake registrations).
        /// </summary>
        public int AuthPermitLimit { get; set; } = 20;

        /// <summary>How many calls of any kind one address may make a minute.</summary>
        public int GeneralPermitLimit { get; set; } = 600;
    }

    public static class AppRateLimiting
    {
        /// <summary>The name of the stricter limit for sign-in style endpoints.</summary>
        public const string AuthPolicy = "auth";

        public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            var settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>() ?? new RateLimitSettings();

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Everyone is counted by address. (Behind a proxy or load balancer the app must be told to trust the
                // forwarded address first, or everyone would look like the proxy.)
                static string Client(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                options.AddPolicy(AuthPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                    Client(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthPermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        Client(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.GeneralPermitLimit,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));

                // The reply says how long to wait, in the same format as every other error.
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.Headers.RetryAfter = "60";
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ProblemDetails
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too many requests",
                            Detail = "Too many requests from this address. Please wait a minute and try again."
                        },
                        cancellationToken);
                };
            });

            return services;
        }
    }
}
