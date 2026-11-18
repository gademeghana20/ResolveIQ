using ResolveIQ.Application.DTOs.Notifications;

namespace ResolveIQ.Application.Abstractions
{
    /// <summary>Reading and clearing one person's notifications. People can only ever touch their own.</summary>
    public interface INotificationService
    {
        /// <summary>The newest notifications (read and unread) and the total number unread.</summary>
        Task<NotificationListResponse> GetAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>Marks one notification read. False if it does not exist or belongs to someone else.</summary>
        Task<bool> MarkReadAsync(string userId, Guid notificationId, CancellationToken cancellationToken = default);

        /// <summary>Marks all of the person's notifications read.</summary>
        Task MarkAllReadAsync(string userId, CancellationToken cancellationToken = default);
    }
}
