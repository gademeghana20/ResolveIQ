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

        /// <summary>Creates a user with the given role (an Admin action).</summary>
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
        /// Deactivates or reactivates a user. Either way, their current login tokens stop working at once.
        /// An Admin cannot deactivate themselves.
        /// </summary>
        Task<UserChangeResult> SetActiveAsync(
            string id,
            bool isActive,
            string actingUserId,
            CancellationToken cancellationToken = default);
    }
}
