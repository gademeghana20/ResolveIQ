namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// A file attached to an incident, and optionally to one of its comments. The file's bytes are kept in the database,
    /// so a backup of the database is a backup of the files too.
    /// </summary>
    public class Attachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Counts up in the order files are saved (filled in by the database), to break ties in time.</summary>
        public long Sequence { get; set; }

        public Guid IncidentId { get; set; }

        /// <summary>The comment it was attached with. Empty when it was attached to the incident itself.</summary>
        public Guid? CommentId { get; set; }

        public string UploadedByUserId { get; set; } = string.Empty;

        /// <summary>A cleaned-up name (no folders, no odd characters). Never used to find the file on any disk.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Chosen by the app from the file's extension, never taken from what the sender said.</summary>
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public byte[] Content { get; set; } = Array.Empty<byte>();

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
