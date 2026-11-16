using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Dashboard;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [HttpGet("summary")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return Ok(await _dashboardService.GetSummaryAsync(userId, roles, cancellationToken));
        }

        [HttpGet("incident-statistics")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<DashboardStatisticsResponse>> GetStatistics(
            [FromQuery] DashboardStatisticsQuery query,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            return Ok(await _dashboardService.GetStatisticsAsync(query.Days, userId, roles, cancellationToken));
        }

        private bool TryGetCurrentUser(out string userId, out IReadOnlyCollection<string> roles)
        {
            userId = User.FindFirstValue("sub") ?? string.Empty;
            roles = User.FindAll("role").Select(c => c.Value).ToList();
            return userId.Length > 0;
        }
    }
}
