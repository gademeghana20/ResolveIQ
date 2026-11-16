using ResolveIQ.Application.DTOs.Sla;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Application.Abstractions
{
    public interface ISlaPolicyService
    {
        Task<IReadOnlyList<SlaPolicyResponse>> GetPoliciesAsync(CancellationToken cancellationToken = default);

        /// <summary>Updates one priority's policy. Returns null if there is no policy for that priority.</summary>
        Task<SlaPolicyResponse?> UpdatePolicyAsync(
            IncidentPriority priority,
            UpdateSlaPolicyRequest request,
            CancellationToken cancellationToken = default);
    }
}
