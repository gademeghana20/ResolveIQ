using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public enum IncidentSortField
    {
        CreatedAt,
        Priority,
        Status,
        IncidentNumber,
        Title
    }

    public enum SortDirection
    {
        Asc,
        Desc
    }

    /// <summary>
    /// Search, filter, sort and paging options for the incident list.
    /// Every filter is optional; leaving them all out lists everything the user may see.
    /// </summary>
    public class IncidentQuery
    {
        public const int MaxPageSize = 100;

        /// <summary>Matches the incident number, title or description.</summary>
        [StringLength(100)]
        public string? Search { get; set; }

        [EnumDataType(typeof(IncidentStatus))]
        public IncidentStatus? Status { get; set; }

        [EnumDataType(typeof(IncidentPriority))]
        public IncidentPriority? Priority { get; set; }

        public Guid? CategoryId { get; set; }

        [StringLength(450)]
        public string? AssignedToUserId { get; set; }

        /// <summary>True lists only incidents nobody has been assigned to.</summary>
        public bool? Unassigned { get; set; }

        /// <summary>True lists only incidents assigned to the current user.</summary>
        public bool AssignedToMe { get; set; }

        /// <summary>True lists only incidents the current user reported.</summary>
        public bool ReportedByMe { get; set; }

        [EnumDataType(typeof(SlaState))]
        public SlaState? SlaState { get; set; }

        [EnumDataType(typeof(IncidentSortField))]
        public IncidentSortField SortBy { get; set; } = IncidentSortField.CreatedAt;

        [EnumDataType(typeof(SortDirection))]
        public SortDirection SortDirection { get; set; } = SortDirection.Desc;

        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        [Range(1, MaxPageSize)]
        public int PageSize { get; set; } = 20;
    }
}
