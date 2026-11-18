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

        /// <summary>How many unfinished incidents (not yet resolved) are assigned to this person.</summary>
        public int ActiveAssignedCount { get; set; }
    }

    /// <summary>
    /// What an Admin sends to create a user with a chosen role. There is no password: the new person is emailed
    /// a link to choose their own, so nobody else ever knows it.
    /// </summary>
    public class CreateUserRequest
    {
        [Required]
        [EmailWithDomain]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

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

    public class SetUserActiveRequest
    {
        public bool IsActive { get; set; }

        /// <summary>
        /// Only when deactivating: hands this person's unfinished incidents to someone else first.
        /// Left out, their incidents stay assigned to them.
        /// </summary>
        [StringLength(450)]
        public string? ReassignToUserId { get; set; }
    }

    public record UserChangeResult(
        ChangeOutcome Outcome,
        UserSummaryResponse? User = null,
        IReadOnlyList<FieldError>? Errors = null);
}
