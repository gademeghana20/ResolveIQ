using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Auth;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IJwtTokenService _tokenService;

        public AuthController(UserManager<IdentityUser> userManager, IJwtTokenService tokenService)
        {
            _userManager = userManager;
            _tokenService = tokenService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
        {
            var user = new IdentityUser
            {
                UserName = request.Email,
                Email = request.Email
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(error.Code, error.Description);
                }

                return ValidationProblem(ModelState);
            }

            // Everyone who registers is an Employee. The client can never choose a role.
            await _userManager.AddToRoleAsync(user, RoleNames.Employee);

            return Ok(await BuildResponseAsync(user));
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
        {
            var invalid = Unauthorized(new { message = "Invalid email or password." });

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return invalid;
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return invalid;
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                await _userManager.AccessFailedAsync(user);
                return invalid;
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            return Ok(await BuildResponseAsync(user));
        }

        private async Task<AuthResponse> BuildResponseAsync(IdentityUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);

            return new AuthResponse
            {
                Token = _tokenService.CreateToken(user.Id, user.Email!, roles, user.SecurityStamp ?? string.Empty),
                Email = user.Email!,
                Roles = roles
            };
        }
    }
}
