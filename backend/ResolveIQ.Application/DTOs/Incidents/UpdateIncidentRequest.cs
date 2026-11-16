using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Application.DTOs.Incidents
{
    public class UpdateIncidentRequest
    {
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(4000)]
        public string Description { get; set; } = string.Empty;
    }
}
