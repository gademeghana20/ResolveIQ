using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// The resolution target for one priority, for example "Critical incidents: 4 hours".
    /// </summary>
    public class SlaPolicy
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public IncidentPriority Priority { get; set; }

        public int ResolutionTargetHours { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
