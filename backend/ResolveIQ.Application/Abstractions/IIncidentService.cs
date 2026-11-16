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
    }
}
