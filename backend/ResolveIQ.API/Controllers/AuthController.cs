using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ResolveIQ.API.Registration;
using ResolveIQ.API.Security;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Auth;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [EnableRateLimiting(AppRateLimiting.AuthPolicy)]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IJwtTokenService _tokenService;
        private readonly IPasswordService _passwords;
        private readonly IRefreshTokenService _refreshTokens;
        private readonly RegistrationSettings _registration;
        private readonly IAuditLog _audit;

        public AuthController(
            UserManager<IdentityUser> userManager,
            IJwtTokenService tokenService,
            IPasswordService passwords,
            IRefreshTokenService refreshTokens,
            IOptions<RegistrationSettings> registration,
            IAuditLog audit)
        {
            _audit = audit;
            _userManager = userManager;
            _tokenService = tokenService;
            _passwords = passwords;
            _refreshTokens = refreshTokens;
            _registration = registration.Value;
        }

        /// <summary>Whether people can create their own account, and if it is limited to certain email domains.</summary>
        [HttpGet("registration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<RegistrationStatusResponse> GetRegistration() =>
            Ok(new RegistrationStatusResponse
            {
                Enabled = _registration.Enabled,
                AllowedEmailDomains = _registration.AllowedEmailDomains
            });

        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
        {
            if (!_registration.Enabled)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Registration is closed",
                    Detail = "People cannot create their own accounts here. Ask an administrator to add you."
                });
            }

            var allowed = _registration.AllowedEmailDomains;
            var domain = request.Email[(request.Email.LastIndexOf('@') + 1)..];
            if (allowed.Length > 0 && !allowed.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(
                    "Email",
                    $"Registration is limited to these email domains: {string.Join(", ", allowed)}.");
                return ValidationProblem(ModelState);
            }

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

            // The person is the one who did it (nobody is signed in yet).
            await _audit.RecordAsync(
                AuditActions.UserRegistered, AuditActions.TargetUser, user.Id, user.Email, "Created their own account",
                actorUserId: user.Id);

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

        /// <summary>
        /// Swaps a refresh token for a new login token and a new refresh token (the old one is used up).
        /// Fails with 401 if the token is unknown, expired or already used, or the account was deactivated
        /// or had its password or role changed since: the person must sign in again.
        /// </summary>
        [HttpPost("refresh-token")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<AuthResponse>> RefreshToken(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _refreshTokens.RotateAsync(request.RefreshToken, cancellationToken);
            if (!result.Succeeded)
            {
                return Unauthorized(new { message = "Please sign in again." });
            }

            var user = await _userManager.FindByIdAsync(result.UserId!);
            return Ok(await BuildResponseAsync(user!, result.NewRefreshToken));
        }

        /// <summary>Signing out: cancels the refresh token so it cannot be used again. Always answers 204.</summary>
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            await _refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
            return NoContent();
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

        // Builds what the app receives at sign-in: a short-lived login token plus a refresh token.
        // A refresh that already made its replacement passes it in; every other sign-in gets a new one.
        private async Task<AuthResponse> BuildResponseAsync(IdentityUser user, string? refreshToken = null)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var stamp = user.SecurityStamp ?? string.Empty;

            return new AuthResponse
            {
                Token = _tokenService.CreateToken(user.Id, user.Email!, roles, stamp),
                RefreshToken = refreshToken ?? await _refreshTokens.IssueAsync(user.Id, stamp),
                Email = user.Email!,
                Roles = roles
            };
        }
    }
}
