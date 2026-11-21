using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Attachments;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Attachments;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class AttachmentService : IAttachmentService
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;
        private readonly INotifier _notifier;
        private readonly AttachmentSettings _settings;
        private readonly DemoSettings _demo;

        public AttachmentService(
            ApplicationDbContext db,
            TimeProvider time,
            INotifier notifier,
            IOptions<AttachmentSettings> settings,
            IOptions<DemoSettings> demo)
        {
            _db = db;
            _time = time;
            _notifier = notifier;
            _settings = settings.Value;
            _demo = demo.Value;
        }

        public AttachmentSettingsResponse GetSettings() => new()
        {
            Enabled = _settings.IsEnabled(_demo),
            MaxFileBytes = _settings.EffectiveMaxFileBytes,
            MaxFilesPerIncident = _settings.MaxFilesPerIncident,
            MaxFilesPerComment = _settings.MaxFilesPerComment,
            AllowedExtensions = AttachmentRules.AllowedTypes.Keys.OrderBy(k => k).ToList()
        };

        public async Task<IReadOnlyList<AttachmentResponse>?> ListAsync(
            Guid incidentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var status = await IncidentAccess.ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .Where(i => i.Id == incidentId)
                .Select(i => (IncidentStatus?)i.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (status is null)
            {
                return null;
            }

            // Only the details are read here: the file's bytes stay in the database until someone downloads the file.
            var rows = await _db.Attachments.AsNoTracking()
                .Where(a => a.IncidentId == incidentId)
                .OrderBy(a => a.CreatedAtUtc)
                .ThenBy(a => a.Sequence)
                .Select(a => new
                {
                    a.Id,
                    a.IncidentId,
                    a.CommentId,
                    a.FileName,
                    a.ContentType,
                    a.SizeBytes,
                    a.UploadedByUserId,
                    UploadedByEmail = _db.Users.Where(u => u.Id == a.UploadedByUserId).Select(u => u.Email).FirstOrDefault(),
                    a.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            var closed = status == IncidentStatus.Closed;
            var canManage = IncidentAccess.IsManagerOrAdmin(roles);

            return rows.Select(a => new AttachmentResponse
            {
                Id = a.Id,
                IncidentId = a.IncidentId,
                CommentId = a.CommentId,
                FileName = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                UploadedByUserId = a.UploadedByUserId,
                UploadedByEmail = a.UploadedByEmail,
                CreatedAtUtc = a.CreatedAtUtc,
                CanRemove = !closed && (a.UploadedByUserId == userId || canManage)
            }).ToList();
        }

        public async Task<AttachmentUploadResult> UploadAsync(
            Guid incidentId,
            Guid? commentId,
            string fileName,
            byte[] content,
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
                return new AttachmentUploadResult(ChangeOutcome.NotFound);
            }

            if (!_settings.IsEnabled(_demo))
            {
                return Invalid("Attaching files is switched off on this site.");
            }

            if (incident.Status == IncidentStatus.Closed)
            {
                return Invalid("Files cannot be attached to a closed incident.");
            }

            var cleanName = AttachmentRules.CleanFileName(fileName);

            var maxBytes = _settings.EffectiveMaxFileBytes;
            if (content.LongLength > maxBytes)
            {
                return Invalid($"That file is too big. The limit is {FormatMegabytes(maxBytes)}.");
            }

            var problem = AttachmentRules.Check(cleanName, content);
            if (problem is not null)
            {
                return Invalid(problem);
            }

            if (commentId is Guid wantedComment)
            {
                var author = await _db.IncidentComments.AsNoTracking()
                    .Where(c => c.Id == wantedComment && c.IncidentId == incidentId)
                    .Select(c => c.AuthorUserId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (author is null)
                {
                    return Invalid("That comment does not belong to this incident.");
                }

                if (author != userId)
                {
                    return new AttachmentUploadResult(
                        ChangeOutcome.Forbidden, Message: "Only the person who wrote a comment can attach files to it.");
                }

                var onComment = await _db.Attachments.CountAsync(a => a.CommentId == wantedComment, cancellationToken);
                if (onComment >= _settings.MaxFilesPerComment)
                {
                    return Invalid($"A comment can carry at most {_settings.MaxFilesPerComment} files.");
                }
            }

            var onIncident = await _db.Attachments.CountAsync(a => a.IncidentId == incidentId, cancellationToken);
            if (onIncident >= _settings.MaxFilesPerIncident)
            {
                return Invalid($"This incident already has the most files it can hold ({_settings.MaxFilesPerIncident}).");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var attachment = new Attachment
            {
                IncidentId = incidentId,
                CommentId = commentId,
                UploadedByUserId = userId,
                FileName = cleanName,
                ContentType = AttachmentRules.ContentTypeFor(cleanName)!,
                SizeBytes = content.LongLength,
                Content = content,
                CreatedAtUtc = now
            };
            _db.Attachments.Add(attachment);

            _db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = incidentId,
                ChangedByUserId = userId,
                FieldChanged = "AttachmentAdded",
                NewValue = cleanName,
                ChangedAtUtc = now
            });

            var email = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            // A file attached together with a comment is already covered by that comment's notification.
            if (commentId is null)
            {
                var label = $"{incident.IncidentNumber} \"{NotificationText.Shorten(incident.Title, 60)}\"";
                var message = $"{email ?? "Someone"} attached a file to {label}.";
                var batch = new NotificationBatch();
                batch.Add(incident.ReportedByUserId, "Comment", message);
                batch.Add(incident.AssignedToUserId, "Comment", message);
                _notifier.Queue(incidentId, batch, exceptUserId: userId);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new AttachmentUploadResult(ChangeOutcome.Success, new AttachmentResponse
            {
                Id = attachment.Id,
                IncidentId = incidentId,
                CommentId = commentId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                UploadedByUserId = userId,
                UploadedByEmail = email,
                CreatedAtUtc = attachment.CreatedAtUtc,
                CanRemove = true
            });
        }

        public async Task<AttachmentFile?> DownloadAsync(
            Guid attachmentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var visibleIncidents = IncidentAccess.ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles).Select(i => i.Id);

            return await _db.Attachments.AsNoTracking()
                .Where(a => a.Id == attachmentId && visibleIncidents.Contains(a.IncidentId))
                .Select(a => new AttachmentFile(a.FileName, a.ContentType, a.Content))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<AttachmentRemoveResult> RemoveAsync(
            Guid attachmentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var file = await _db.Attachments.AsNoTracking()
                .Where(a => a.Id == attachmentId)
                .Select(a => new { a.Id, a.IncidentId, a.FileName, a.UploadedByUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (file is null)
            {
                return new AttachmentRemoveResult(ChangeOutcome.NotFound);
            }

            var status = await IncidentAccess.ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .Where(i => i.Id == file.IncidentId)
                .Select(i => (IncidentStatus?)i.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (status is null)
            {
                return new AttachmentRemoveResult(ChangeOutcome.NotFound);
            }

            if (status == IncidentStatus.Closed)
            {
                return new AttachmentRemoveResult(ChangeOutcome.Invalid, "Files cannot be removed from a closed incident.");
            }

            if (file.UploadedByUserId != userId && !IncidentAccess.IsManagerOrAdmin(roles))
            {
                return new AttachmentRemoveResult(ChangeOutcome.Forbidden, "Only the person who attached a file, or a Manager or Admin, can remove it.");
            }

            // Deleting by key only needs the key, so the file's bytes are never read just to delete them.
            var stub = new Attachment { Id = file.Id };
            _db.Attachments.Attach(stub);
            _db.Attachments.Remove(stub);

            _db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = file.IncidentId,
                ChangedByUserId = userId,
                FieldChanged = "AttachmentRemoved",
                OldValue = file.FileName,
                ChangedAtUtc = _time.GetUtcNow().UtcDateTime
            });

            await _db.SaveChangesAsync(cancellationToken);
            return new AttachmentRemoveResult(ChangeOutcome.Success);
        }

        private static AttachmentUploadResult Invalid(string message) => new(ChangeOutcome.Invalid, Message: message);

        private static string FormatMegabytes(long bytes) => $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }
}
