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
        public async Task<ActionResult<IReadOnlyList<IncidentSummaryResponse>>> GetIncidents(
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue("sub");
            if (userId is null)
            {
                return Unauthorized();
            }

            var roles = User.FindAll("role").Select(c => c.Value).ToList();

            var incidents = await _incidentService.GetIncidentsAsync(userId, roles, cancellationToken);
            return Ok(incidents);
        }
    }
}
