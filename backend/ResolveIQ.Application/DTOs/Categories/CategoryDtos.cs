using System.ComponentModel.DataAnnotations;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.Application.DTOs.Categories
{
    public class CategoryResponse
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool IsActive { get; set; }
    }

    public class CreateCategoryRequest
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }
    }

    public class UpdateCategoryRequest
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>False hides the category from new incidents. Old incidents keep it.</summary>
        public bool IsActive { get; set; } = true;
    }

    public record CategoryChangeResult(
        ChangeOutcome Outcome,
        CategoryResponse? Category = null,
        IReadOnlyList<FieldError>? Errors = null);
}
