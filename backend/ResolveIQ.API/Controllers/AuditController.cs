using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Audit;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.API.Controllers
{
    /// <summary>The audit trail of what Admins (and new sign-ups) did. Only Admins may read it.</summary>
    [ApiController]
    [Authorize(Roles = RoleNames.Admin)]
    [Route("api/audit")]
    public class AuditController : ControllerBase
    {
        private readonly IAuditService _audit;

        public AuditController(IAuditService audit)
        {
            _audit = audit;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PagedResult<AuditLogResponse>>> GetAudit(
            [FromQuery] AuditQuery query,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(query.Action) && !AuditActions.All.Contains(query.Action.Trim()))
            {
                ModelState.AddModelError(nameof(query.Action), "That is not a known action.");
                return ValidationProblem(ModelState);
            }

            return Ok(await _audit.GetAsync(query, cancellationToken));
        }
    }
}
