using ResolveIQ.Application.Abstractions;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class Notifier : INotifier
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;

        public Notifier(ApplicationDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public void Queue(Guid? incidentId, NotificationBatch batch, string? exceptUserId)
        {
            var now = _time.GetUtcNow().UtcDateTime;

            foreach (var (userId, kind, message) in batch.For(exceptUserId))
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = userId,
                    IncidentId = incidentId,
                    Kind = kind,
                    Message = message.Length > 500 ? message[..499] + "…" : message,
                    CreatedAtUtc = now
                });
            }
        }
    }

    /// <summary>The wording shared by everything that sends a notification.</summary>
    internal static class NotificationText
    {
        /// <summary>"INC-000012 "Printer jams"": the incident, in a few words.</summary>
        public static string Label(Incident incident) => $"{incident.IncidentNumber} \"{Shorten(incident.Title, 60)}\"";

        public static string Shorten(string text, int length) =>
            text.Length <= length ? text : text[..(length - 1)].TrimEnd() + "…";

        public static string StatusName(IncidentStatus status) => status switch
        {
            IncidentStatus.InProgress => "in progress",
            IncidentStatus.OnHold => "on hold",
            _ => status.ToString().ToLowerInvariant()
        };
    }
}
