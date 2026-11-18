namespace ResolveIQ.API.Security
{
    /// <summary>
    /// Adds the standard "browser safety" headers to every response, so a browser that talks to this API is told not to
    /// guess content types, not to put it in a frame, not to send referrers, and (in production) to insist on HTTPS.
    /// </summary>
    public static class SecurityHeaders
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool allowInteractiveDocs)
        {
            return app.Use(async (context, next) =>
            {
                // OnStarting runs just before the response goes out, so the headers also reach errors, 401s and 429s.
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    var path = context.Request.Path;

                    headers["X-Content-Type-Options"] = "nosniff";              // do not guess what a file is
                    headers["X-Frame-Options"] = "DENY";                        // never show this inside someone else's page
                    headers["Referrer-Policy"] = "no-referrer";                 // do not tell other sites where the person came from
                    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                    headers["Cross-Origin-Opener-Policy"] = "same-origin";

                    // This API only ever sends data (JSON), never pages with scripts, so the strictest policy fits.
                    // The developer-only docs pages (Swagger) are the one exception: they need their own scripts.
                    var isDocs = allowInteractiveDocs && (path.StartsWithSegments("/swagger") || path.StartsWithSegments("/openapi"));
                    if (!isDocs)
                    {
                        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
                    }

                    // Sign-in answers carry tokens: never keep a copy in a browser or proxy cache.
                    if (path.StartsWithSegments("/api/auth"))
                    {
                        headers["Cache-Control"] = "no-store";
                    }

                    return Task.CompletedTask;
                });

                await next();
            });
        }
    }
}
