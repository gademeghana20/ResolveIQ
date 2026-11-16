using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class ChangePriorityRequest
    {
        [EnumDataType(typeof(IncidentPriority))]
        public IncidentPriority Priority { get; set; }
    }
}
