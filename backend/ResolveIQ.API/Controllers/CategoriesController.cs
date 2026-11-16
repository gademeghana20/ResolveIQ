using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Categories;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/categories")]
    public class CategoriesController : ControllerBase
    {
        private readonly ICategoryService _categoryService;

        public CategoriesController(ICategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        /// <summary>
        /// Everyone logged in can list the categories (for the "new incident" form).
        /// Only Admins can ask for inactive ones too.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetCategories(
            [FromQuery] bool includeInactive,
            CancellationToken cancellationToken)
        {
            var includeAll = includeInactive && User.IsInRole(RoleNames.Admin);
            return Ok(await _categoryService.GetCategoriesAsync(includeAll, cancellationToken));
        }

        [HttpPost]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<CategoryResponse>> CreateCategory(
            CreateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _categoryService.CreateCategoryAsync(request, cancellationToken);

            return result.Outcome == ChangeOutcome.Success
                ? StatusCode(StatusCodes.Status201Created, result.Category)
                : ToProblem(result);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CategoryResponse>> UpdateCategory(
            Guid id,
            UpdateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _categoryService.UpdateCategoryAsync(id, request, cancellationToken);

            return result.Outcome == ChangeOutcome.Success ? Ok(result.Category) : ToProblem(result);
        }

        private ActionResult ToProblem(CategoryChangeResult result)
        {
            if (result.Outcome == ChangeOutcome.NotFound)
            {
                return NotFound();
            }

            foreach (var error in result.Errors ?? Array.Empty<ResolveIQ.Application.DTOs.Common.FieldError>())
            {
                ModelState.AddModelError(error.Field, error.Message);
            }

            return ValidationProblem(ModelState);
        }
    }
}
