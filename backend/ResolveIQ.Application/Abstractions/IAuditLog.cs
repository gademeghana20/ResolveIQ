using ResolveIQ.Application.DTOs.Audit;
using ResolveIQ.Application.DTOs.Common;

namespace ResolveIQ.Application.Abstractions
{
    /// <summary>Who is making the current request (read from their sign-in), so the audit trail can name them.</summary>
    public interface ICurrentActor
    {
        string? UserId { get; }
    }

    /// <summary>Writes lines to the audit trail.</summary>
    public interface IAuditLog
    {
        /// <summary>
        /// Adds one line and saves it. The actor is the signed-in person unless <paramref name="actorUserId"/>
        /// names someone else (for example a person who just created their own account).
        /// </summary>
        Task RecordAsync(
            string action,
            string targetType,
            string? targetId,
            string? targetLabel,
            string? details = null,
            string? actorUserId = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Reads the audit trail (Admins only).</summary>
    public interface IAuditService
    {
        Task<PagedResult<AuditLogResponse>> GetAsync(AuditQuery query, CancellationToken cancellationToken = default);
    }
}
