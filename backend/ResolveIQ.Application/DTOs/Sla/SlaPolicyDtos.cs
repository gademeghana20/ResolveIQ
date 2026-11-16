using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Sla
{
    public class SlaPolicyResponse
    {
        public IncidentPriority Priority { get; set; }

        public int ResolutionTargetHours { get; set; }

        public bool IsActive { get; set; }
    }

    public class UpdateSlaPolicyRequest
    {
        [Range(1, 720)]
        public int ResolutionTargetHours { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
