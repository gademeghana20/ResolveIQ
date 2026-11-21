using ResolveIQ.Application.DTOs.Incidents;

namespace ResolveIQ.Application.DTOs.Attachments
{
    /// <summary>What an attached file looks like in a list. The file's bytes are fetched separately, only when asked for.</summary>
    public class AttachmentResponse
    {
        public Guid Id { get; set; }

        public Guid IncidentId { get; set; }

        /// <summary>Empty when the file was attached to the incident itself rather than to a comment.</summary>
        public Guid? CommentId { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public string UploadedByUserId { get; set; } = string.Empty;

        public string? UploadedByEmail { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Whether the person asking may remove it (they uploaded it, or they are a Manager or Admin, and the incident is open).</summary>
        public bool CanRemove { get; set; }
    }

    /// <summary>What the web app needs to know before it shows the "attach files" controls.</summary>
    public class AttachmentSettingsResponse
    {
        /// <summary>False means uploads are switched off on this site (existing files can still be downloaded).</summary>
        public bool Enabled { get; set; }

        public long MaxFileBytes { get; set; }

        public int MaxFilesPerIncident { get; set; }

        public int MaxFilesPerComment { get; set; }

        public IReadOnlyList<string> AllowedExtensions { get; set; } = Array.Empty<string>();
    }

    /// <summary>The outcome of attaching a file. For Invalid, Message says what was wrong.</summary>
    public record AttachmentUploadResult(
        ChangeOutcome Outcome,
        AttachmentResponse? Attachment = null,
        string? Message = null);

    /// <summary>The outcome of removing a file. For Invalid or Forbidden, Message says why.</summary>
    public record AttachmentRemoveResult(ChangeOutcome Outcome, string? Message = null);

    /// <summary>A file ready to be sent to the person who asked for it.</summary>
    public record AttachmentFile(string FileName, string ContentType, byte[] Content);
}
