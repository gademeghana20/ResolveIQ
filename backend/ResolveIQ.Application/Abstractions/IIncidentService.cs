using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Enums;

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
        /// Assigns (triages) an incident: picks who owns it and, if it has none yet, sets its priority.
        /// The first assignment starts the SLA clock. The caller must already have checked that the
        /// acting user is allowed to assign.
        /// </summary>
        Task<IncidentChangeResult> AssignIncidentAsync(
            Guid id,
            string assigneeUserId,
            IncidentPriority? priority,
            string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves an incident to a new status, if the workflow rules allow this user to do so.
        /// Putting an incident OnHold needs a reason and pauses the SLA clock.
        /// </summary>
        Task<IncidentChangeResult> ChangeStatusAsync(
            Guid id,
            IncidentStatus newStatus,
            string? reason,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes an incident's priority. Only Admins and Managers may, and not on a closed incident.
        /// </summary>
        Task<IncidentChangeResult> ChangePriorityAsync(
            Guid id,
            IncidentPriority newPriority,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Edits an incident's title and description. Allowed for the reporter and for Admins and
        /// Managers, until the incident is closed.
        /// </summary>
        Task<IncidentChangeResult> UpdateIncidentAsync(
            Guid id,
            UpdateIncidentRequest request,
            string userId,
            IReadOnlyCollection<string> roles,
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
