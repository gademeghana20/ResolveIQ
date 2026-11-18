using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Categories;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class CategoryService : ICategoryService
    {
        private const string DuplicateMessage = "A category with this name already exists.";

        private readonly ApplicationDbContext _db;
        private readonly IAuditLog _audit;

        public CategoryService(ApplicationDbContext db, IAuditLog audit)
        {
            _db = db;
            _audit = audit;
        }

        public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken = default)
        {
            var categories = await _db.Categories.AsNoTracking()
                .Where(c => includeInactive || c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            return categories.Select(ToResponse).ToList();
        }

        public async Task<CategoryChangeResult> CreateCategoryAsync(
            CreateCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                return Invalid("Name", "The name cannot be empty.");
            }

            // The database compares names without caring about capital letters, so "hardware" counts as "Hardware".
            if (await _db.Categories.AnyAsync(c => c.Name == name, cancellationToken))
            {
                return Invalid("Name", DuplicateMessage);
            }

            var category = new Category
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
            };

            _db.Categories.Add(category);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(
                AuditActions.CategoryCreated, AuditActions.TargetCategory, category.Id.ToString(), category.Name,
                cancellationToken: cancellationToken);

            return new CategoryChangeResult(ChangeOutcome.Success, ToResponse(category));
        }

        public async Task<CategoryChangeResult> UpdateCategoryAsync(
            Guid id,
            UpdateCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (category is null)
            {
                return new CategoryChangeResult(ChangeOutcome.NotFound);
            }

            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                return Invalid("Name", "The name cannot be empty.");
            }

            if (await _db.Categories.AnyAsync(c => c.Name == name && c.Id != id, cancellationToken))
            {
                return Invalid("Name", DuplicateMessage);
            }

            var oldName = category.Name;
            var oldDescription = category.Description;
            var oldActive = category.IsActive;

            category.Name = name;
            category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            category.IsActive = request.IsActive;

            await _db.SaveChangesAsync(cancellationToken);

            var changes = new List<string>();
            if (oldName != category.Name)
            {
                changes.Add($"Name: {oldName} → {category.Name}");
            }

            if (oldDescription != category.Description)
            {
                changes.Add("Description changed");
            }

            if (oldActive != category.IsActive)
            {
                changes.Add(category.IsActive ? "Shown again" : "Hidden");
            }

            if (changes.Count > 0)
            {
                await _audit.RecordAsync(
                    AuditActions.CategoryUpdated, AuditActions.TargetCategory, category.Id.ToString(), category.Name,
                    string.Join("; ", changes), cancellationToken: cancellationToken);
            }

            return new CategoryChangeResult(ChangeOutcome.Success, ToResponse(category));
        }

        private static CategoryChangeResult Invalid(string field, string message) =>
            new(ChangeOutcome.Invalid, Errors: new[] { new FieldError(field, message) });

        private static CategoryResponse ToResponse(Category c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            IsActive = c.IsActive
        };
    }
}
