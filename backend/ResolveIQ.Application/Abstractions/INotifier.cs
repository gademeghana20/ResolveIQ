namespace ResolveIQ.Application.Abstractions
{
    /// <summary>
    /// The messages one event should send, collected before anything is saved. A person gets at most one message per
    /// event (the first one added for them), so someone who is both the reporter and the assignee is not told twice.
    /// </summary>
    public sealed class NotificationBatch
    {
        private readonly Dictionary<string, (string Kind, string Message)> _byUser = new();

        public void Add(string? userId, string kind, string message)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                _byUser.TryAdd(userId, (kind, message));
            }
        }

        /// <summary>The messages, leaving out the person who caused the event (they know already).</summary>
        public IEnumerable<(string UserId, string Kind, string Message)> For(string? exceptUserId) =>
            _byUser
                .Where(entry => entry.Key != exceptUserId)
                .Select(entry => (entry.Key, entry.Value.Kind, entry.Value.Message));
    }

    public interface INotifier
    {
        /// <summary>
        /// Queues the batch for saving. Nothing is written yet: the messages are saved by the caller's own SaveChanges,
        /// in the same transaction as the change they describe, so a message exists exactly when the change does.
        /// </summary>
        void Queue(Guid? incidentId, NotificationBatch batch, string? exceptUserId);
    }
}
