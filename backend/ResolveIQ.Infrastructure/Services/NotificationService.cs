using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Notifications;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        /// <summary>How many of the newest are sent back. The bell shows a handful; the unread count covers them all.</summary>
        public const int PageSize = 30;

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;

        public NotificationService(ApplicationDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public async Task<NotificationListResponse> GetAsync(string userId, CancellationToken cancellationToken = default)
        {
            var mine = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

            var unread = await mine.CountAsync(n => n.ReadAtUtc == null, cancellationToken);

            var items = await mine
                .OrderByDescending(n => n.CreatedAtUtc)
                .ThenByDescending(n => n.Sequence)
                .Take(PageSize)
                .Select(n => new NotificationResponse
                {
                    Id = n.Id,
                    Kind = n.Kind,
                    Message = n.Message,
                    IncidentId = n.IncidentId,
                    CreatedAtUtc = n.CreatedAtUtc,
                    IsRead = n.ReadAtUtc != null
                })
                .ToListAsync(cancellationToken);

            return new NotificationListResponse { UnreadCount = unread, Items = items };
        }

        public async Task<bool> MarkReadAsync(string userId, Guid notificationId, CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;

            // Someone else's notification looks exactly like one that does not exist.
            var exists = await _db.Notifications.AnyAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
            if (!exists)
            {
                return false;
            }

            await _db.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId && n.ReadAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), cancellationToken);

            return true;
        }

        public async Task MarkAllReadAsync(string userId, CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;

            await _db.Notifications
                .Where(n => n.UserId == userId && n.ReadAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), cancellationToken);
        }
    }
}
