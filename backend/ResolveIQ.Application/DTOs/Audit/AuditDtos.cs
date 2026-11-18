using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Application.DTOs.Audit
{
    public class AuditLogResponse
    {
        public Guid Id { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public string? ActorEmail { get; set; }

        public string Action { get; set; } = string.Empty;

        public string TargetType { get; set; } = string.Empty;

        public string? TargetLabel { get; set; }

        public string? Details { get; set; }
    }

    public class AuditQuery
    {
        public const int MaxPageSize = 100;

        /// <summary>One of the known actions, such as user.role_changed. Empty lists all.</summary>
        [StringLength(50)]
        public string? Action { get; set; }

        /// <summary>Matches the person who did it, what it was done to, or the details.</summary>
        [StringLength(100)]
        public string? Search { get; set; }

        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        [Range(1, MaxPageSize)]
        public int PageSize { get; set; } = 20;
    }
}
