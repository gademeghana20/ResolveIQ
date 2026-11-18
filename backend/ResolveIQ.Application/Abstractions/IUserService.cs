using ResolveIQ.Application.DTOs.Users;

namespace ResolveIQ.Application.Abstractions
{
    public interface IUserService
    {
        /// <summary>
        /// Lists all users with their roles and whether they are active,
        /// for example to choose who an incident is assigned to.
        /// </summary>
        Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(CancellationToken cancellationToken = default);

        /// <summary>Returns one user, or null if there is none with that id.</summary>
        Task<UserSummaryResponse?> GetUserAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a user with the given role (an Admin action). The Admin does not choose a password: the new person is
        /// emailed a link to set their own.
        /// </summary>
        Task<UserChangeResult> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gives a user a new role (replacing their old one). An Admin cannot change their own role,
        /// which guarantees there is always at least one Admin.
        /// </summary>
        Task<UserChangeResult> ChangeRoleAsync(
            string id,
            string role,
            string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Emails a person a link to choose a new password (an Admin action, for example after it was forgotten).
        /// The Admin never sees or types a password. Fails for a deactivated person, who gets no email.
        /// </summary>
        Task<UserChangeResult> SendPasswordResetEmailAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deactivates or reactivates a user. Either way, their current login tokens stop working at once.
        /// An Admin cannot deactivate themselves. When deactivating, the person's unfinished incidents can be
        /// handed to <paramref name="reassignToUserId"/> in the same step.
        /// </summary>
        Task<UserChangeResult> SetActiveAsync(
            string id,
            bool isActive,
            string actingUserId,
            string? reassignToUserId = null,
            CancellationToken cancellationToken = default);
    }
}
