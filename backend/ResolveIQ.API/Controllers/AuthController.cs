using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
        private readonly IPasswordService _passwords;

        public AuthController(
            UserManager<IdentityUser> userManager,
            IJwtTokenService tokenService,
            IPasswordService passwords)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _passwords = passwords;
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

        /// <summary>Change your own password. You stay signed in (a fresh token comes back); other logins end.</summary>
        [HttpPost("change-password")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<AuthResponse>> ChangePassword(
            ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue("sub") ?? string.Empty;

            var result = await _passwords.ChangePasswordAsync(
                userId, request.CurrentPassword, request.NewPassword, cancellationToken);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            // The change ended every earlier token, including the one that made this request, so hand back a new one.
            var user = await _userManager.FindByIdAsync(userId);
            return Ok(await BuildResponseAsync(user!));
        }

        /// <summary>
        /// Asks for a password-reset email. The answer is always the same, whether or not the address has
        /// an account, so this cannot be used to find out who is registered.
        /// </summary>
        [HttpPost("forgot-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<MessageResponse>> ForgotPassword(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken)
        {
            await _passwords.SendForgottenLinkAsync(request.Email, cancellationToken);

            return Ok(new MessageResponse
            {
                Message = "If that address has an account, an email with a link to choose a new password is on its way."
            });
        }

        /// <summary>Chooses a new password using the one-time token from the emailed link.</summary>
        [HttpPost("reset-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MessageResponse>> ResetPassword(
            ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _passwords.ResetPasswordAsync(
                request.Email, request.Token, request.NewPassword, cancellationToken);

            return result.Succeeded
                ? Ok(new MessageResponse { Message = "Your password was changed. You can sign in now." })
                : ToProblem(result);
        }

        private ActionResult ToProblem(PasswordResult result)
        {
            foreach (var error in result.Errors ?? Array.Empty<ResolveIQ.Application.DTOs.Common.FieldError>())
            {
                ModelState.AddModelError(error.Field, error.Message);
            }

            return ValidationProblem(ModelState);
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
