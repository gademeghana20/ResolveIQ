using Microsoft.Extensions.Options;
using ResolveIQ.Infrastructure.Services;

namespace ResolveIQ.API.Maintenance
{
    /// <summary>
    /// Runs the housekeeping (<see cref="MaintenanceJobs"/>) on a timer for as long as the app is running.
    /// A failed run is logged and tried again next time; it never brings the app down.
    /// </summary>
    public class MaintenanceService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly MaintenanceSettings _settings;
        private readonly ILogger<MaintenanceService> _logger;

        public MaintenanceService(
            IServiceScopeFactory scopes,
            IOptions<MaintenanceSettings> settings,
            ILogger<MaintenanceService> logger)
        {
            _scopes = scopes;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes)));

            do
            {
                try
                {
                    // A fresh scope each time: the jobs use the database, which must not be held for the app's lifetime.
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<MaintenanceJobs>().RunAllAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Maintenance run failed; it will be tried again at the next interval.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
