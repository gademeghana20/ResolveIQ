using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
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
        private readonly INotifier _notifier;
        private readonly MaintenanceSettings _settings;
        private readonly ILogger<MaintenanceJobs> _logger;

        public MaintenanceJobs(
            ApplicationDbContext db,
            TimeProvider time,
            INotifier notifier,
            IOptions<MaintenanceSettings> settings,
            ILogger<MaintenanceJobs> logger)
        {
            _db = db;
            _time = time;
            _notifier = notifier;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task RunAllAsync(CancellationToken cancellationToken = default)
        {
            var tokens = await DeleteExpiredRefreshTokensAsync(cancellationToken);
            var closed = await CloseStaleResolvedIncidentsAsync(cancellationToken);
            var notifications = await DeleteOldNotificationsAsync(cancellationToken);

            if (tokens + closed + notifications > 0)
            {
                _logger.LogInformation(
                    "Maintenance: removed {Tokens} expired refresh tokens and {Notifications} old notifications, closed {Closed} stale resolved incidents.",
                    tokens,
                    notifications,
                    closed);
            }
        }

        /// <summary>
        /// Closes incidents that have stayed Resolved for the configured number of days without the reporter confirming the
        /// fix or reopening it. The history says the app did it, and the reporter and assignee are told. Returns how many.
        /// </summary>
        public async Task<int> CloseStaleResolvedIncidentsAsync(CancellationToken cancellationToken = default)
        {
            var days = _settings.AutoCloseResolvedAfterDays;
            if (days <= 0)
            {
                return 0;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var cutoff = now.AddDays(-days);

            var stale = await _db.Incidents
                .Where(i => i.Status == IncidentStatus.Resolved && i.ResolvedAtUtc != null && i.ResolvedAtUtc <= cutoff)
                .OrderBy(i => i.ResolvedAtUtc)
                .Take(200)
                .ToListAsync(cancellationToken);

            var closed = 0;

            foreach (var incident in stale)
            {
                incident.Status = IncidentStatus.Closed;
                incident.ClosedAtUtc = now;
                incident.UpdatedAtUtc = now;

                _db.IncidentHistory.Add(new IncidentHistory
                {
                    IncidentId = incident.Id,
                    ChangedByUserId = null,
                    FieldChanged = "Status",
                    OldValue = IncidentStatus.Resolved.ToString(),
                    NewValue = IncidentStatus.Closed.ToString(),
                    ChangedAtUtc = now
                });
                _db.IncidentHistory.Add(new IncidentHistory
                {
                    IncidentId = incident.Id,
                    ChangedByUserId = null,
                    FieldChanged = "AutoClosed",
                    NewValue = $"Closed automatically after {days} days resolved",
                    ChangedAtUtc = now
                });

                var message = $"{NotificationText.Label(incident)} was closed automatically: it stayed resolved for {days} days.";
                var batch = new NotificationBatch();
                batch.Add(incident.ReportedByUserId, "AutoClosed", message);
                batch.Add(incident.AssignedToUserId, "AutoClosed", message);
                _notifier.Queue(incident.Id, batch, exceptUserId: null);

                try
                {
                    // One at a time, so an incident that someone changed at this very moment is skipped, not the whole run.
                    await _db.SaveChangesAsync(cancellationToken);
                    closed++;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Forget only what this incident had pending (its change, history and notifications). The rest of the list is untouched.
                    foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                    {
                        entry.State = entry.State == EntityState.Added ? EntityState.Detached : EntityState.Unchanged;
                    }
                }
            }

            return closed;
        }

        /// <summary>Removes notifications read more than 30 days ago, and any (read or not) older than 90 days.</summary>
        public Task<int> DeleteOldNotificationsAsync(CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var readCutoff = now.AddDays(-30);
            var anyCutoff = now.AddDays(-90);

            return _db.Notifications
                .Where(n => n.CreatedAtUtc < anyCutoff || (n.ReadAtUtc != null && n.ReadAtUtc < readCutoff))
                .ExecuteDeleteAsync(cancellationToken);
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
