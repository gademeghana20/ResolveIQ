using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class AssignIncidentRequest
    {
        [Required]
        [StringLength(450)]
        public string AssigneeUserId { get; set; } = string.Empty;
    }
}
