using ResolveIQ.Application.DTOs.Categories;

namespace ResolveIQ.Application.Abstractions
{
    public interface ICategoryService
    {
        /// <summary>Lists categories by name. Inactive ones are left out unless asked for.</summary>
        Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken = default);

        Task<CategoryChangeResult> CreateCategoryAsync(
            CreateCategoryRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames, re-describes, or deactivates a category. Categories are never deleted,
        /// because old incidents still point to them.
        /// </summary>
        Task<CategoryChangeResult> UpdateCategoryAsync(
            Guid id,
            UpdateCategoryRequest request,
            CancellationToken cancellationToken = default);
    }
}
