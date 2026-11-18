using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    /// <summary>
    /// What a reporter sends. There is deliberately no priority: a Manager or Admin sets it when assigning
    /// (triaging) the incident. The reporter can say how urgent it feels, as a hint for that decision.
    /// </summary>
    public class CreateIncidentRequest
    {
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(4000)]
        public string Description { get; set; } = string.Empty;

        public Guid? CategoryId { get; set; }

        /// <summary>How urgent this is for the reporter (optional). Only a hint: it starts no SLA clock.</summary>
        [EnumDataType(typeof(IncidentPriority))]
        public IncidentPriority? Urgency { get; set; }
    }
}
