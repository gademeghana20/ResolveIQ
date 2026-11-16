using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Comments;
using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/incidents/{incidentId:guid}/comments")]
    public class CommentsController : ControllerBase
    {
        private readonly ICommentService _commentService;

        public CommentsController(ICommentService commentService)
        {
            _commentService = commentService;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<CommentResponse>>> GetComments(
            Guid incidentId,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            var comments = await _commentService.GetCommentsAsync(incidentId, userId, roles, cancellationToken);
            return comments is null ? NotFound() : Ok(comments);
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CommentResponse>> AddComment(
            Guid incidentId,
            CreateCommentRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            var result = await _commentService.AddCommentAsync(
                incidentId, request.Text, userId, roles, cancellationToken);

            switch (result.Outcome)
            {
                case ChangeOutcome.NotFound:
                    return NotFound();

                case ChangeOutcome.Invalid:
                    ModelState.AddModelError(nameof(request.Text), result.Message ?? "Invalid comment.");
                    return ValidationProblem(ModelState);

                default:
                    return CreatedAtAction(nameof(GetComments), new { incidentId }, result.Comment);
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
