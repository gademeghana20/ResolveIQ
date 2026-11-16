using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Workflow;

namespace ResolveIQ.Application.DTOs.Incidents
{
    /// <summary>
    /// Works out what a user may do with an incident, using the same Domain rules the change
    /// endpoints use. It only describes what will be accepted; the endpoints still enforce it.
    /// </summary>
    public static class IncidentPermissions
    {
        public static IncidentActionsResponse For(
            IncidentDetailResponse incident,
            string userId,
            IReadOnlyCollection<string> roles)
        {
            var isManagerOrAdmin = roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Manager);
            var isAssignee = incident.AssignedToUserId == userId;
            var isReporter = incident.ReportedByUserId == userId;
            var hasAssignee = incident.AssignedToUserId is not null;

            var allowedStatuses = Enum.GetValues<IncidentStatus>()
                .Where(target => target != incident.Status
                    && IncidentWorkflow.CheckStatusChange(
                        incident.Status, target, isManagerOrAdmin, isAssignee, isReporter, hasAssignee)
                        == StatusChangeCheck.Allowed)
                .ToList();

            return new IncidentActionsResponse
            {
                CanAssign = isManagerOrAdmin && !IncidentWorkflow.IsClosed(incident.Status),
                CanChangePriority = isManagerOrAdmin && IncidentWorkflow.CanChangePriority(incident.Status),
                AllowedStatusChanges = allowedStatuses
            };
        }
    }
}
