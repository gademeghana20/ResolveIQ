namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// A note someone added to an incident's conversation. Comments are a record: they are not edited or deleted.
    /// </summary>
    public class IncidentComment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid IncidentId { get; set; }

        public string AuthorUserId { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
