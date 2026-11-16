using System.ComponentModel.DataAnnotations;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class CreateIncidentRequest
    {
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(4000)]
        public string Description { get; set; } = string.Empty;

        [EnumDataType(typeof(IncidentPriority))]
        public IncidentPriority Priority { get; set; }

        public Guid? CategoryId { get; set; }
    }
}
