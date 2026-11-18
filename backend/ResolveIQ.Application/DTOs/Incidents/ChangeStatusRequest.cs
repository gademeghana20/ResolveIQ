using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class ChangeStatusRequest
    {
        [EnumDataType(typeof(IncidentStatus))]
        public IncidentStatus Status { get; set; }

        /// <summary>
        /// Required when putting an incident OnHold (why is work paused? up to 500 characters) and when marking it
        /// Resolved (how was it fixed? up to 2000 characters).
        /// </summary>
        [StringLength(2000)]
        public string? Reason { get; set; }
    }
}
