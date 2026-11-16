using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class ChangeStatusRequest
    {
        [EnumDataType(typeof(IncidentStatus))]
        public IncidentStatus Status { get; set; }
    }
}
