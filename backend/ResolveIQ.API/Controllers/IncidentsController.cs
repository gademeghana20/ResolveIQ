using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;

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
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<PagedResult<IncidentSummaryResponse>>> GetIncidents(
            [FromQuery] IncidentQuery query,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return Ok(await _incidentService.GetIncidentsAsync(query, userId, roles, cancellationToken));
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

        [HttpPut("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<IncidentDetailResponse>> UpdateIncident(
            Guid id,
            UpdateIncidentRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return ToActionResult(await _incidentService.UpdateIncidentAsync(id, request, userId, roles, cancellationToken));
        }

        [HttpPut("{id:guid}/assign")]
        [Authorize(Roles = RoleNames.AdminOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<IncidentDetailResponse>> AssignIncident(
            Guid id,
            AssignIncidentRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out _))
            {
                return Unauthorized();
            }

            return ToActionResult(await _incidentService.AssignIncidentAsync(
                id, request.AssigneeUserId, request.Priority, userId, cancellationToken));
        }

        [HttpPut("{id:guid}/status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<IncidentDetailResponse>> ChangeStatus(
            Guid id,
            ChangeStatusRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return ToActionResult(await _incidentService.ChangeStatusAsync(
                id, request.Status, request.Reason, userId, roles, cancellationToken));
        }

        [HttpPut("{id:guid}/priority")]
        [Authorize(Roles = RoleNames.AdminOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<IncidentDetailResponse>> ChangePriority(
            Guid id,
            ChangePriorityRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return ToActionResult(await _incidentService.ChangePriorityAsync(
                id, request.Priority, userId, roles, cancellationToken));
        }

        [HttpGet("{id:guid}/history")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<IncidentHistoryResponse>>> GetHistory(
            Guid id,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            var history = await _incidentService.GetHistoryAsync(id, userId, roles, cancellationToken);
            return history is null ? NotFound() : Ok(history);
        }

        // One place that turns a service outcome into an HTTP response.
        private ActionResult<IncidentDetailResponse> ToActionResult(IncidentChangeResult result)
        {
            switch (result.Outcome)
            {
                case ChangeOutcome.NotFound:
                    return NotFound();

                case ChangeOutcome.Forbidden:
                    return Forbid();

                case ChangeOutcome.Invalid:
                    ModelState.AddModelError(result.Field ?? string.Empty, result.Message ?? "Invalid request.");
                    return ValidationProblem(ModelState);

                case ChangeOutcome.Conflict:
                    return Conflict(new ProblemDetails
                    {
                        Title = "The incident was changed by someone else. Reload it and try again.",
                        Status = StatusCodes.Status409Conflict
                    });

                default:
                    return Ok(result.Incident);
            }
        }

        private bool TryGetCurrentUser(out string userId, out IReadOnlyCollection<string> roles)
        {
            userId = User.FindFirstValue("sub") ?? string.Empty;
            roles = User.FindAll("role").Select(c => c.Value).ToList();
            return userId.Length > 0;
        }
    }
}
