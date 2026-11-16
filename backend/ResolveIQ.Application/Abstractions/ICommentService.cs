using ResolveIQ.Application.DTOs.Comments;

namespace ResolveIQ.Application.Abstractions
{
    public interface ICommentService
    {
        /// <summary>
        /// Returns an incident's comments, oldest first, or null if the incident does not exist
        /// or the user is not allowed to see it.
        /// </summary>
        Task<IReadOnlyList<CommentResponse>?> GetCommentsAsync(
            Guid incidentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a comment. Anyone who can see the incident may comment, except on a closed incident.
        /// </summary>
        Task<AddCommentResult> AddCommentAsync(
            Guid incidentId,
            string text,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);
    }
}
