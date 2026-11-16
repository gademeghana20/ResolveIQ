namespace ResolveIQ.Application.DTOs.Incidents
{
    public enum ChangeOutcome
    {
        Success,
        NotFound,
        Forbidden,
        Invalid,
        Conflict
    }

    /// <summary>
    /// The result of changing an incident (assign, status, priority, edit).
    /// For Invalid, Field and Message say what was wrong.
    /// </summary>
    public record IncidentChangeResult(
        ChangeOutcome Outcome,
        IncidentDetailResponse? Incident = null,
        string? Field = null,
        string? Message = null);
}
