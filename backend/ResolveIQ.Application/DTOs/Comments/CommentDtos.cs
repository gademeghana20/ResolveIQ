using System.ComponentModel.DataAnnotations;
using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.Application.DTOs.Comments
{
    public class CreateCommentRequest
    {
        [Required]
        [StringLength(2000)]
        public string Text { get; set; } = string.Empty;
    }

    public class CommentResponse
    {
        public Guid Id { get; set; }

        public string Text { get; set; } = string.Empty;

        public string AuthorUserId { get; set; } = string.Empty;

        public string? AuthorEmail { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>The outcome of adding a comment. For Invalid, Message says what was wrong.</summary>
    public record AddCommentResult(
        ChangeOutcome Outcome,
        CommentResponse? Comment = null,
        string? Message = null);
}
