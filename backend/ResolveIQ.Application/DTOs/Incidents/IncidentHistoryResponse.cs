namespace ResolveIQ.Application.DTOs.Incidents
{
    public class IncidentHistoryResponse
    {
        public string FieldChanged { get; set; } = string.Empty;

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        public string? ChangedBy { get; set; }

        public DateTime ChangedAtUtc { get; set; }
    }
}
