using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Application.DTOs.Users;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.API.Controllers
{
    // Managers can look people up (to assign incidents); only Admins can change them.
    [ApiController]
    [Authorize(Roles = RoleNames.AdminOrManager)]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<UserSummaryResponse>>> GetUsers(
            CancellationToken cancellationToken)
        {
            return Ok(await _userService.GetUsersAsync(cancellationToken));
        }

        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserSummaryResponse>> GetUser(string id, CancellationToken cancellationToken)
        {
            var user = await _userService.GetUserAsync(id, cancellationToken);
            return user is null ? NotFound() : Ok(user);
        }

        // A method-level [Authorize] adds to the class-level one, so these need Admin AND (Admin or Manager) = Admin only.

        [HttpPost]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<UserSummaryResponse>> CreateUser(
            CreateUserRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _userService.CreateUserAsync(request, cancellationToken);

            return result.Outcome == ChangeOutcome.Success
                ? CreatedAtAction(nameof(GetUser), new { id = result.User!.Id }, result.User)
                : ToProblem(result);
        }

        [HttpPut("{id}/role")]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserSummaryResponse>> ChangeRole(
            string id,
            ChangeUserRoleRequest request,
            CancellationToken cancellationToken)
        {
            var actingUserId = User.FindFirstValue("sub") ?? string.Empty;
            var result = await _userService.ChangeRoleAsync(id, request.Role, actingUserId, cancellationToken);

            return result.Outcome == ChangeOutcome.Success ? Ok(result.User) : ToProblem(result);
        }

        // Sends the person an email with a link to choose a new password. The Admin never handles a password.
        [HttpPost("{id}/password-reset-email")]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserSummaryResponse>> SendPasswordResetEmail(
            string id,
            CancellationToken cancellationToken)
        {
            var result = await _userService.SendPasswordResetEmailAsync(id, cancellationToken);

            return result.Outcome == ChangeOutcome.Success ? Ok(result.User) : ToProblem(result);
        }

        [HttpPut("{id}/active")]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserSummaryResponse>> SetActive(
            string id,
            SetUserActiveRequest request,
            CancellationToken cancellationToken)
        {
            var actingUserId = User.FindFirstValue("sub") ?? string.Empty;
            var result = await _userService.SetActiveAsync(id, request.IsActive, actingUserId, cancellationToken);

            return result.Outcome == ChangeOutcome.Success ? Ok(result.User) : ToProblem(result);
        }

        private ActionResult ToProblem(UserChangeResult result)
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
