namespace ResolveIQ.Application.DTOs.Incidents
{
    public enum AssignOutcome
    {
        Assigned,
        IncidentNotFound,
        AssigneeNotFound,
        Conflict
    }

    public record AssignIncidentResult(AssignOutcome Outcome, IncidentDetailResponse? Incident = null);
}
