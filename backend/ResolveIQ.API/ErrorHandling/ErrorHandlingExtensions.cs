namespace ResolveIQ.API.ErrorHandling
{
    public static class ErrorHandlingExtensions
    {
        /// <summary>Registers the services for consistent error responses.</summary>
        public static IServiceCollection AddGlobalErrorHandling(this IServiceCollection services)
        {
            services.AddProblemDetails();
            services.AddExceptionHandler<GlobalExceptionHandler>();
            return services;
        }

        /// <summary>
        /// Turns crashes into a clean 500, and empty error responses (401, 403, 404...) into the same
        /// standard "problem details" format. Add this at the very start of the pipeline.
        /// </summary>
        public static IApplicationBuilder UseGlobalErrorHandling(this IApplicationBuilder app)
        {
            app.UseExceptionHandler();
            app.UseStatusCodePages();
            return app;
        }
    }
}
