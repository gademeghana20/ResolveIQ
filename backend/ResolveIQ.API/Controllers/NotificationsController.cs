using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Notifications;

namespace ResolveIQ.API.Controllers
{
    /// <summary>The bell: a person's own notifications. Everyone signed in has them; nobody can see anyone else's.</summary>
    [ApiController]
    [Authorize]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notifications;

        public NotificationsController(INotificationService notifications)
        {
            _notifications = notifications;
        }

        private string UserId => User.FindFirstValue("sub") ?? string.Empty;

        /// <summary>The newest notifications, and how many are unread in all.</summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<NotificationListResponse>> Get(CancellationToken cancellationToken) =>
            Ok(await _notifications.GetAsync(UserId, cancellationToken));

        [HttpPut("{id:guid}/read")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
            await _notifications.MarkReadAsync(UserId, id, cancellationToken) ? NoContent() : NotFound();

        [HttpPut("read-all")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
        {
            await _notifications.MarkAllReadAsync(UserId, cancellationToken);
            return NoContent();
        }
    }
}
