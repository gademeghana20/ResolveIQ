using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Auth;
using ResolveIQ.Application.DTOs.Common;

namespace ResolveIQ.Infrastructure.Services
{
    public class PasswordService : IPasswordService
    {
        private const string BadLinkMessage = "This link is not valid any more. Ask for a new one.";

        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _email;
        private readonly AppSettings _settings;
        private readonly TimeProvider _time;

        public PasswordService(
            UserManager<IdentityUser> userManager,
            IEmailSender email,
            IOptions<AppSettings> settings,
            TimeProvider time)
        {
            _userManager = userManager;
            _email = email;
            _settings = settings.Value;
            _time = time;
        }

        public async Task<PasswordResult> ChangePasswordAsync(
            string userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default)
        {
            var wrong = PasswordResult.Fail("CurrentPassword", "The current password is not correct.");

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || await _userManager.IsLockedOutAsync(user))
            {
                return wrong;
            }

            // A wrong guess counts like a wrong guess at the login screen, so someone holding a stolen
            // login cannot try passwords here without limit.
            if (!await _userManager.CheckPasswordAsync(user, currentPassword))
            {
                await _userManager.AccessFailedAsync(user);
                return wrong;
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            // Also changes the security stamp, which ends every other login this person has.
            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            return result.Succeeded ? PasswordResult.Ok : Rejected(result);
        }

        public async Task SendForgottenLinkAsync(string email, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user is not null && IsActive(user))
            {
                await SendAsync(user, PasswordLinkKind.Forgotten, cancellationToken);
            }
        }

        public async Task<bool> SendLinkAsync(string userId, PasswordLinkKind kind, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !IsActive(user))
            {
                return false;
            }

            await SendAsync(user, kind, cancellationToken);
            return true;
        }

        public async Task<PasswordResult> ResetPasswordAsync(
            string email,
            string token,
            string newPassword,
            CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user is null || !IsActive(user))
            {
                // The same answer as a bad token: nothing reveals whether the address has an account.
                return PasswordResult.Fail("Token", BadLinkMessage);
            }

            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
            {
                return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                    ? PasswordResult.Fail("Token", BadLinkMessage)
                    : Rejected(result);
            }

            // They can sign in with the new password at once: forget earlier failed attempts and any short lock-out.
            await _userManager.ResetAccessFailedCountAsync(user);
            if (user.LockoutEnd is { } lockedUntil && lockedUntil < DateTimeOffset.MaxValue)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            return PasswordResult.Ok;
        }

        // ---- helpers ----

        // A deactivated person is "locked until the end of time" (see UserService); they get no links.
        private bool IsActive(IdentityUser user) => !(user.LockoutEnd > _time.GetUtcNow());

        private static PasswordResult Rejected(IdentityResult result) =>
            new(false, result.Errors.Select(e => new FieldError("NewPassword", e.Description)).ToList());

        private async Task SendAsync(IdentityUser user, PasswordLinkKind kind, CancellationToken cancellationToken)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = $"{_settings.FrontendUrl.TrimEnd('/')}/reset-password" +
                       $"?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

            var (subject, intro) = kind switch
            {
                PasswordLinkKind.Invitation => (
                    "Welcome to ResolveIQ: set your password",
                    "An account was created for you on ResolveIQ. Choose your password with this link:"),
                PasswordLinkKind.AdminRequested => (
                    "ResolveIQ: choose a new password",
                    "An administrator asked for you to choose a new ResolveIQ password. Use this link:"),
                _ => (
                    "ResolveIQ: reset your password",
                    "Someone asked to reset the password for this ResolveIQ account. If it was you, use this link:")
            };

            var body = $"{intro}\n\n{link}\n\n" +
                       "The link works once and expires after a short time. " +
                       "If you did not expect this email, you can ignore it; nothing changes.";

            await _email.SendAsync(user.Email!, subject, body, cancellationToken);
        }
    }
}
