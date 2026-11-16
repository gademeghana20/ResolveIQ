using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Sla;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Route("api/sla/policies")]
    public class SlaController : ControllerBase
    {
        private readonly ISlaPolicyService _policyService;

        public SlaController(ISlaPolicyService policyService)
        {
            _policyService = policyService;
        }

        [HttpGet]
        [Authorize(Roles = RoleNames.AdminOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<SlaPolicyResponse>>> GetPolicies(
            CancellationToken cancellationToken)
        {
            return Ok(await _policyService.GetPoliciesAsync(cancellationToken));
        }

        [HttpPut("{priority}")]
        [Authorize(Roles = RoleNames.Admin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SlaPolicyResponse>> UpdatePolicy(
            IncidentPriority priority,
            UpdateSlaPolicyRequest request,
            CancellationToken cancellationToken)
        {
            // An invalid priority (such as "Banana" or 99) is already rejected with a 400 before this runs.
            var updated = await _policyService.UpdatePolicyAsync(priority, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
    }
}
