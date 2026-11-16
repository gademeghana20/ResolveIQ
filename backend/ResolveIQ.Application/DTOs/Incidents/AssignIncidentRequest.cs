using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class AssignIncidentRequest
    {
        [Required]
        [StringLength(450)]
        public string AssigneeUserId { get; set; } = string.Empty;

        /// <summary>
        /// Required when the incident has no priority yet. If the incident already has one,
        /// sending a different value changes it (and restarts the SLA clock).
        /// </summary>
        [EnumDataType(typeof(IncidentPriority))]
        public IncidentPriority? Priority { get; set; }
    }
}
