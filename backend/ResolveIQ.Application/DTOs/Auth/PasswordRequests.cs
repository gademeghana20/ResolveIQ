using System.ComponentModel.DataAnnotations;
using ResolveIQ.Application.DTOs.Common;

namespace ResolveIQ.Application.DTOs.Auth
{
    /// <summary>A signed-in person changing their own password. They must prove they know the current one.</summary>
    public class ChangePasswordRequest
    {
        [Required]
        [StringLength(100)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>Someone who cannot sign in asks for a reset link to be emailed to them.</summary>
    public class ForgotPasswordRequest
    {
        // The lenient check, like login: older accounts with unusual addresses must still be able to reset.
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>Choosing a new password with the one-time token from the emailed link.</summary>
    public class ResetPasswordRequest
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        public string Token { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>A generic answer for password requests that must not reveal whether an account exists.</summary>
    public class MessageResponse
    {
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>The result of a password operation: worked, or the field-by-field reasons it did not.</summary>
    public record PasswordResult(bool Succeeded, IReadOnlyList<FieldError>? Errors = null)
    {
        public static PasswordResult Ok { get; } = new(true);

        public static PasswordResult Fail(string field, string message) =>
            new(false, new[] { new FieldError(field, message) });
    }

    /// <summary>Why a password link is being emailed (it changes the wording of the email).</summary>
    public enum PasswordLinkKind
    {
        /// <summary>The person asked for it themselves ("Forgot password").</summary>
        Forgotten,

        /// <summary>An Admin created the account, so the person sets their first password.</summary>
        Invitation,

        /// <summary>An Admin asked for the person to choose a new password.</summary>
        AdminRequested
    }
}
