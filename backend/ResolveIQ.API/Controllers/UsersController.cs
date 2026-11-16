using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Users;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.API.Controllers
{
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
    }
}
