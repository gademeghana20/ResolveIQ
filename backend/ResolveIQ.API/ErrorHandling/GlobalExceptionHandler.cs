using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ResolveIQ.API.ErrorHandling
{
    /// <summary>
    /// The last line of defence: any exception nobody else handled ends up here.
    /// It logs the full details for developers and sends the client a short, safe message.
    /// </summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IProblemDetailsService _problemDetailsService;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionHandler(
            ILogger<GlobalExceptionHandler> logger,
            IProblemDetailsService problemDetailsService,
            IHostEnvironment environment)
        {
            _logger = logger;
            _problemDetailsService = problemDetailsService;
            _environment = environment;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            // Developers get everything in the log (with the stack trace).
            _logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            };

            // Only while developing do we show the message. Never the stack trace, and never in production.
            if (_environment.IsDevelopment())
            {
                problem.Detail = exception.Message;
            }

            return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception
            });
        }
    }
}
