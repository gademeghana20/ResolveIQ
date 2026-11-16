using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.Application.Abstractions
{
    public interface IIncidentService
    {
        /// <summary>
        /// Lists the incidents the given user is allowed to see.
        /// Admins and Managers see all; others see only incidents they reported or are assigned to.
        /// </summary>
        Task<IReadOnlyList<IncidentSummaryResponse>> GetIncidentsAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns one incident, or null if it does not exist or the user is not allowed to see it.
        /// </summary>
        Task<IncidentDetailResponse?> GetIncidentByIdAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an Open, unassigned incident reported by the given user.
        /// Returns null if the request names a category that does not exist or is inactive.
        /// </summary>
        Task<IncidentDetailResponse?> CreateIncidentAsync(
            string reportedByUserId,
            CreateIncidentRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Assigns an incident to a user and records the change in the incident's history.
        /// The caller must already have checked that the acting user is allowed to assign.
        /// </summary>
        Task<AssignIncidentResult> AssignIncidentAsync(
            Guid id,
            string assigneeUserId,
            string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns an incident's change history, oldest first, or null if the incident
        /// does not exist or the user is not allowed to see it.
        /// </summary>
        Task<IReadOnlyList<IncidentHistoryResponse>?> GetHistoryAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);
    }
}
