using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Domain.Entities
{
    public class Incident
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string IncidentNumber { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Null until a Manager or Admin sets it when assigning (triaging) the incident.
        /// </summary>
        public IncidentPriority? Priority { get; set; }

        public IncidentStatus Status { get; set; } = IncidentStatus.Open;

        public Guid? CategoryId { get; set; }

        public Category? Category { get; set; }

        public string ReportedByUserId { get; set; } = string.Empty;

        public string? AssignedToUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        /// <summary>When the incident was closed (by its reporter, a Manager or Admin, or automatically). Empty until then.</summary>
        public DateTime? ClosedAtUtc { get; set; }

        /// <summary>
        /// How urgent the reporter said it was when reporting it. Only a hint for whoever triages it: the real priority,
        /// and with it the SLA, is still set by a Manager or Admin.
        /// </summary>
        public IncidentPriority? ReportedUrgency { get; set; }

        /// <summary>How it was fixed, written when it was resolved. Cleared if the incident is reopened (the history keeps it).</summary>
        public string? ResolutionNotes { get; set; }

        /// <summary>
        /// When the SLA clock started: the first assignment, or the latest priority change.
        /// Null while the incident has never been assigned.
        /// </summary>
        public DateTime? SlaStartedAtUtc { get; set; }

        /// <summary>
        /// The number of hours promised when the clock last started (the first assignment, or a priority
        /// change), taken from the SLA policy of that moment. Later policy changes never touch it, so an
        /// incident keeps the deadline it was given and a finished incident keeps its Met or Breached result.
        /// Null while the clock has never started, or when no active policy applied (that means "no SLA").
        /// </summary>
        public int? SlaTargetHours { get; set; }

        /// <summary>
        /// Set while the incident is OnHold (the clock is paused); null otherwise.
        /// </summary>
        public DateTime? SlaPausedAtUtc { get; set; }

        /// <summary>
        /// Total time, in seconds, the clock has been paused (OnHold, or Resolved before a reopen)
        /// since it last started. This time does not count against the SLA.
        /// </summary>
        public long SlaPausedSeconds { get; set; }

        /// <summary>
        /// Changes automatically on every update. Lets the database detect two people
        /// editing the same incident at the same time.
        /// </summary>
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
