using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Comments;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class CommentService : ICommentService
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;
        private readonly INotifier _notifier;

        public CommentService(ApplicationDbContext db, TimeProvider time, INotifier notifier)
        {
            _db = db;
            _time = time;
            _notifier = notifier;
        }

        public async Task<IReadOnlyList<CommentResponse>?> GetCommentsAsync(
            Guid incidentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var canSee = await IncidentAccess.ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .AnyAsync(i => i.Id == incidentId, cancellationToken);
            if (!canSee)
            {
                return null;
            }

            return await _db.IncidentComments.AsNoTracking()
                .Where(c => c.IncidentId == incidentId)
                .OrderBy(c => c.CreatedAtUtc)
                .ThenBy(c => c.Sequence)
                .Select(c => new CommentResponse
                {
                    Id = c.Id,
                    Text = c.Text,
                    AuthorUserId = c.AuthorUserId,
                    AuthorEmail = _db.Users
                        .Where(u => u.Id == c.AuthorUserId)
                        .Select(u => u.Email)
                        .FirstOrDefault(),
                    CreatedAtUtc = c.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<AddCommentResult> AddCommentAsync(
            Guid incidentId,
            string text,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            // Not found and not allowed look the same, so nobody can probe for incident ids.
            var incident = await IncidentAccess.ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .Where(i => i.Id == incidentId)
                .Select(i => new { i.Status, i.IncidentNumber, i.Title, i.ReportedByUserId, i.AssignedToUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (incident is null)
            {
                return new AddCommentResult(ChangeOutcome.NotFound);
            }

            if (incident.Status == IncidentStatus.Closed)
            {
                return new AddCommentResult(ChangeOutcome.Invalid, Message: "Comments are closed on a closed incident.");
            }

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return new AddCommentResult(ChangeOutcome.Invalid, Message: "A comment cannot be empty.");
            }

            var comment = new IncidentComment
            {
                IncidentId = incidentId,
                AuthorUserId = userId,
                Text = trimmed,
                CreatedAtUtc = _time.GetUtcNow().UtcDateTime
            };

            _db.IncidentComments.Add(comment);

            var authorEmail = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            // The reporter and the assignee are told (never the person who wrote it), in the same save as the comment.
            var label = $"{incident.IncidentNumber} \"{NotificationText.Shorten(incident.Title, 60)}\"";
            var message = $"{authorEmail ?? "Someone"} commented on {label}.";
            var batch = new NotificationBatch();
            batch.Add(incident.ReportedByUserId, "Comment", message);
            batch.Add(incident.AssignedToUserId, "Comment", message);
            _notifier.Queue(incidentId, batch, exceptUserId: userId);

            await _db.SaveChangesAsync(cancellationToken);

            return new AddCommentResult(ChangeOutcome.Success, new CommentResponse
            {
                Id = comment.Id,
                Text = comment.Text,
                AuthorUserId = comment.AuthorUserId,
                AuthorEmail = authorEmail,
                CreatedAtUtc = comment.CreatedAtUtc
            });
        }
    }
}
