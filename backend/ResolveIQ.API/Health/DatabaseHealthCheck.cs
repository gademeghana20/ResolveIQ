using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.API.Health
{
    /// <summary>
    /// "Can the app reach its database?" This is what a hosting service asks to decide whether to send traffic here.
    /// The answer says only Healthy or Unhealthy: never the reason, which could reveal server names or settings.
    /// </summary>
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _db;

        public DatabaseHealthCheck(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _db.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy();
            }
            catch
            {
                return HealthCheckResult.Unhealthy();
            }
        }
    }
}
