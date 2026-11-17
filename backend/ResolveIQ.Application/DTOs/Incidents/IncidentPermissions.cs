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
                CanEdit = isReporter && !IncidentWorkflow.IsClosed(incident.Status),
                CanAssign = isManagerOrAdmin && !IncidentWorkflow.IsClosed(incident.Status),
                CanChangePriority = isManagerOrAdmin && IncidentWorkflow.CanChangePriority(incident.Status),
                AllowedStatusChanges = allowedStatuses,
                OnBehalfOf = allowedStatuses
                    .Select(target => OnBehalf(incident, target, isManagerOrAdmin, isAssignee, isReporter))
                    .OfType<StatusChangeOnBehalf>()
                    .ToList()
            };
        }

        // A manager or admin may make any valid move, but if the move is really the assignee's or the
        // reporter's job (and the user is not that person), they are stepping in on that person's behalf.
        private static StatusChangeOnBehalf? OnBehalf(
            IncidentDetailResponse incident,
            IncidentStatus target,
            bool isManagerOrAdmin,
            bool isAssignee,
            bool isReporter)
        {
            var from = incident.Status;
            var isOwnMove = (isAssignee && IncidentWorkflow.IsAssigneeMove(from, target))
                            || (isReporter && IncidentWorkflow.IsReporterMove(from, target));

            if (!isManagerOrAdmin || isOwnMove)
            {
                return null;
            }

            if (IncidentWorkflow.IsAssigneeMove(from, target))
            {
                return new StatusChangeOnBehalf { Status = target, Role = "Assignee", Email = incident.AssignedToEmail };
            }

            if (IncidentWorkflow.IsReporterMove(from, target))
            {
                return new StatusChangeOnBehalf { Status = target, Role = "Reporter", Email = incident.ReportedByEmail };
            }

            return null;
        }
    }
}
