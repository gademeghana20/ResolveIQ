using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class ChangeStatusRequest
    {
        [EnumDataType(typeof(IncidentStatus))]
        public IncidentStatus Status { get; set; }

        /// <summary>Required when putting an incident OnHold (why is work paused?).</summary>
        [StringLength(500)]
        public string? Reason { get; set; }
    }
}
