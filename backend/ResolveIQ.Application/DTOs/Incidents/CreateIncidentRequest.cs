using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Application.DTOs.Incidents
{
    /// <summary>
    /// What a reporter sends. There is deliberately no priority: a Manager or Admin sets it
    /// when assigning (triaging) the incident.
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
    }
}
