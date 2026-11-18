using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>
    /// The housekeeping the app does for itself: tidying data that would otherwise pile up forever.
    /// Each job is its own method so it can be tested directly; <see cref="RunAllAsync"/> runs them in turn.
    /// </summary>
    public class MaintenanceJobs
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;
        private readonly ILogger<MaintenanceJobs> _logger;

        public MaintenanceJobs(ApplicationDbContext db, TimeProvider time, ILogger<MaintenanceJobs> logger)
        {
            _db = db;
            _time = time;
            _logger = logger;
        }

        public async Task RunAllAsync(CancellationToken cancellationToken = default)
        {
            var tokens = await DeleteExpiredRefreshTokensAsync(cancellationToken);

            if (tokens > 0)
            {
                _logger.LogInformation("Maintenance: removed {Count} expired refresh tokens.", tokens);
            }
        }

        /// <summary>
        /// Removes refresh tokens that ran out. They can never be used again, so keeping them only makes the table grow.
        /// (A token that was used up but has not expired is kept until it does, because seeing one used a second time
        /// is how a stolen copy is noticed.)
        /// </summary>
        public Task<int> DeleteExpiredRefreshTokensAsync(CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;

            return _db.RefreshTokens
                .Where(t => t.ExpiresAtUtc < now)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
