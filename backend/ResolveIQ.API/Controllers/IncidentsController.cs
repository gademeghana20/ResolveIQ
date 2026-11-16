using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/incidents")]
    public class IncidentsController : ControllerBase
    {
        private readonly IIncidentService _incidentService;

        public IncidentsController(IIncidentService incidentService)
        {
            _incidentService = incidentService;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IReadOnlyList<IncidentSummaryResponse>>> GetIncidents(
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return Ok(await _incidentService.GetIncidentsAsync(userId, roles, cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IncidentDetailResponse>> GetIncident(
            Guid id,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            // Not found and not allowed look the same, so nobody can probe for incident ids.
            var incident = await _incidentService.GetIncidentByIdAsync(id, userId, roles, cancellationToken);
            return incident is null ? NotFound() : Ok(incident);
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IncidentDetailResponse>> CreateIncident(
            CreateIncidentRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out _))
            {
                return Unauthorized();
            }

            var created = await _incidentService.CreateIncidentAsync(userId, request, cancellationToken);
            if (created is null)
            {
                ModelState.AddModelError(
                    nameof(request.CategoryId),
                    "The category does not exist or is not active.");
                return ValidationProblem(ModelState);
            }

            return CreatedAtAction(nameof(GetIncident), new { id = created.Id }, created);
        }

        private bool TryGetCurrentUser(out string userId, out IReadOnlyCollection<string> roles)
        {
            userId = User.FindFirstValue("sub") ?? string.Empty;
            roles = User.FindAll("role").Select(c => c.Value).ToList();
            return userId.Length > 0;
        }
    }
}
