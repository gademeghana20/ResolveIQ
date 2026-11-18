using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Audit;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>Writes the audit trail, and reads it for Admins.</summary>
    public class AuditLogService : IAuditLog, IAuditService
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;
        private readonly ICurrentActor _actor;

        public AuditLogService(ApplicationDbContext db, TimeProvider time, ICurrentActor actor)
        {
            _db = db;
            _time = time;
            _actor = actor;
        }

        public async Task RecordAsync(
            string action,
            string targetType,
            string? targetId,
            string? targetLabel,
            string? details = null,
            string? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            var actorId = actorUserId ?? _actor.UserId;
            var actorEmail = actorId is null
                ? null
                : await _db.Users.AsNoTracking()
                    .Where(u => u.Id == actorId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync(cancellationToken);

            _db.AuditLogs.Add(new AuditLog
            {
                CreatedAtUtc = _time.GetUtcNow().UtcDateTime,
                ActorUserId = actorId,
                ActorEmail = actorEmail,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                TargetLabel = Cut(targetLabel, 256),
                Details = Cut(details, 1000)
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<PagedResult<AuditLogResponse>> GetAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            var lines = _db.AuditLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var action = query.Action.Trim();
                lines = lines.Where(a => a.Action == action);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var text = query.Search.Trim();
                lines = lines.Where(a =>
                    (a.ActorEmail != null && a.ActorEmail.Contains(text)) ||
                    (a.TargetLabel != null && a.TargetLabel.Contains(text)) ||
                    (a.Details != null && a.Details.Contains(text)));
            }

            var total = await lines.CountAsync(cancellationToken);

            var page = await lines
                .OrderByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Sequence)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(a => new AuditLogResponse
                {
                    Id = a.Id,
                    CreatedAtUtc = a.CreatedAtUtc,
                    ActorEmail = a.ActorEmail,
                    Action = a.Action,
                    TargetType = a.TargetType,
                    TargetLabel = a.TargetLabel,
                    Details = a.Details
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<AuditLogResponse>
            {
                Items = page,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = total
            };
        }

        private static string? Cut(string? text, int length) =>
            text is not null && text.Length > length ? text[..(length - 1)] + "…" : text;
    }
}
