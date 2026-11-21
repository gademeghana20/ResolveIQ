using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Attachments;
using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.API.Controllers
{
    /// <summary>
    /// Files attached to incidents and comments. Every call is checked against who may see the incident, exactly like
    /// the incident itself, so a file is as private as the incident it belongs to.
    /// </summary>
    [ApiController]
    [Authorize]
    public class AttachmentsController : ControllerBase
    {
        // A little above the biggest file allowed, for the form's own wrapping. The service enforces the real limit.
        private const long MaxRequestBytes = 11L * 1024 * 1024;

        private readonly IAttachmentService _attachments;

        public AttachmentsController(IAttachmentService attachments)
        {
            _attachments = attachments;
        }

        /// <summary>The limits and the allowed kinds of file, so the web app can show the right controls.</summary>
        [HttpGet("api/attachments/settings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<AttachmentSettingsResponse> GetSettings() => Ok(_attachments.GetSettings());

        [HttpGet("api/incidents/{incidentId:guid}/attachments")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<AttachmentResponse>>> List(
            Guid incidentId,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            var list = await _attachments.ListAsync(incidentId, userId, roles, cancellationToken);
            return list is null ? NotFound() : Ok(list);
        }

        /// <summary>Attaches one file (send it as a form with the field "file"; "commentId" is optional).</summary>
        [HttpPost("api/incidents/{incidentId:guid}/attachments")]
        [RequestSizeLimit(MaxRequestBytes)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AttachmentResponse>> Upload(
            Guid incidentId,
            IFormFile file,
            [FromForm] Guid? commentId,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            if (file is null || file.Length == 0)
            {
                ModelState.AddModelError("file", "Choose a file to attach.");
                return ValidationProblem(ModelState);
            }

            // Refuse a huge file before reading any of it into memory. The exact limit is checked by the service.
            if (file.Length > MaxRequestBytes)
            {
                ModelState.AddModelError("file", "That file is too big.");
                return ValidationProblem(ModelState);
            }

            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);

            var result = await _attachments.UploadAsync(
                incidentId, commentId, file.FileName, buffer.ToArray(), userId, roles, cancellationToken);

            switch (result.Outcome)
            {
                case ChangeOutcome.NotFound:
                    return NotFound();

                case ChangeOutcome.Forbidden:
                    return Problem(
                        title: "Not allowed",
                        detail: result.Message,
                        statusCode: StatusCodes.Status403Forbidden);

                case ChangeOutcome.Invalid:
                    ModelState.AddModelError("file", result.Message ?? "That file cannot be attached.");
                    return ValidationProblem(ModelState);

                default:
                    return CreatedAtAction(nameof(List), new { incidentId }, result.Attachment);
            }
        }

        /// <summary>Downloads a file. It is always sent as a download (never opened inside the page).</summary>
        [HttpGet("api/attachments/{attachmentId:guid}/download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Download(Guid attachmentId, CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            var file = await _attachments.DownloadAsync(attachmentId, userId, roles, cancellationToken);
            if (file is null)
            {
                return NotFound();
            }

            // Giving File() a download name makes the answer an "attachment" (a download), with the name safely encoded.
            Response.Headers.CacheControl = "private, no-store";
            return File(file.Content, file.ContentType, file.FileName);
        }

        [HttpDelete("api/attachments/{attachmentId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Remove(Guid attachmentId, CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUser(out var userId, out var roles))
            {
                return Unauthorized();
            }

            var result = await _attachments.RemoveAsync(attachmentId, userId, roles, cancellationToken);

            switch (result.Outcome)
            {
                case ChangeOutcome.NotFound:
                    return NotFound();

                case ChangeOutcome.Forbidden:
                    return Problem(
                        title: "Not allowed",
                        detail: result.Message,
                        statusCode: StatusCodes.Status403Forbidden);

                case ChangeOutcome.Invalid:
                    ModelState.AddModelError("file", result.Message ?? "That file cannot be removed.");
                    return ValidationProblem(ModelState);

                default:
                    return NoContent();
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
