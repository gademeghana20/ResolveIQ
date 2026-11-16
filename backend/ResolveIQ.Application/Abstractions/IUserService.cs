using ResolveIQ.Application.DTOs.Users;

namespace ResolveIQ.Application.Abstractions
{
    public interface IUserService
    {
        /// <summary>
        /// Lists all users with their roles, for example to choose who an incident is assigned to.
        /// </summary>
        Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(CancellationToken cancellationToken = default);
    }
}
