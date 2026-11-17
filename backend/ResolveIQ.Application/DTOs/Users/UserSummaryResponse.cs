using System.ComponentModel.DataAnnotations;
using ResolveIQ.Application.Validation;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.Application.DTOs.Users
{
    public class UserSummaryResponse
    {
        public string Id { get; set; } = string.Empty;

        public string? Email { get; set; }

        public IList<string> Roles { get; set; } = new List<string>();

        /// <summary>False for a deactivated user, who can no longer log in or use the API.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>What an Admin sends to create a user with a chosen role.</summary>
    public class CreateUserRequest
    {
        [Required]
        [EmailWithDomain]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Admin, Manager or Employee.</summary>
        [Required]
        [StringLength(50)]
        public string Role { get; set; } = string.Empty;
    }

    public class ChangeUserRoleRequest
    {
        /// <summary>Admin, Manager or Employee.</summary>
        [Required]
        [StringLength(50)]
        public string Role { get; set; } = string.Empty;
    }

    /// <summary>What an Admin sends to give a user a new password (for example when it was forgotten).</summary>
    public class ResetUserPasswordRequest
    {
        [Required]
        [StringLength(100)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class SetUserActiveRequest
    {
        public bool IsActive { get; set; }
    }

    public record UserChangeResult(
        ChangeOutcome Outcome,
        UserSummaryResponse? User = null,
        IReadOnlyList<FieldError>? Errors = null);
}
