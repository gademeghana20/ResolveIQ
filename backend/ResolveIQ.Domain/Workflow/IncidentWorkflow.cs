using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Domain.Workflow
{
    public enum StatusChangeCheck
    {
        Allowed,
        TransitionNotAllowed,
        AssigneeRequired,
        NotPermitted
    }

    /// <summary>
    /// The business rules for moving an incident between statuses. Pure logic with no database,
    /// so it is fast to unit test.
    /// </summary>
    public static class IncidentWorkflow
    {
        // Which status may follow which. Closed is final.
        private static readonly Dictionary<IncidentStatus, IncidentStatus[]> Transitions = new()
        {
            [IncidentStatus.Open] = [IncidentStatus.InProgress],
            [IncidentStatus.InProgress] = [IncidentStatus.OnHold, IncidentStatus.Resolved],
            [IncidentStatus.OnHold] = [IncidentStatus.InProgress],
            [IncidentStatus.Resolved] = [IncidentStatus.Closed, IncidentStatus.InProgress],
            [IncidentStatus.Closed] = []
        };

        // The person working on the incident.
        private static readonly HashSet<(IncidentStatus From, IncidentStatus To)> AssigneeMoves = new()
        {
            (IncidentStatus.Open, IncidentStatus.InProgress),
            (IncidentStatus.InProgress, IncidentStatus.OnHold),
            (IncidentStatus.OnHold, IncidentStatus.InProgress),
            (IncidentStatus.InProgress, IncidentStatus.Resolved)
        };

        // The person who reported it confirms the fix, or says it is not fixed.
        private static readonly HashSet<(IncidentStatus From, IncidentStatus To)> ReporterMoves = new()
        {
            (IncidentStatus.Resolved, IncidentStatus.Closed),
            (IncidentStatus.Resolved, IncidentStatus.InProgress)
        };

        public static bool IsTransitionAllowed(IncidentStatus from, IncidentStatus to) =>
            Transitions.TryGetValue(from, out var next) && next.Contains(to);

        public static StatusChangeCheck CheckStatusChange(
            IncidentStatus from,
            IncidentStatus to,
            bool isManagerOrAdmin,
            bool isAssignee,
            bool isReporter,
            bool hasAssignee)
        {
            if (!IsTransitionAllowed(from, to))
            {
                return StatusChangeCheck.TransitionNotAllowed;
            }

            if (to == IncidentStatus.InProgress && !hasAssignee)
            {
                return StatusChangeCheck.AssigneeRequired;
            }

            if (isManagerOrAdmin
                || (isAssignee && AssigneeMoves.Contains((from, to)))
                || (isReporter && ReporterMoves.Contains((from, to))))
            {
                return StatusChangeCheck.Allowed;
            }

            return StatusChangeCheck.NotPermitted;
        }

        /// <summary>A closed incident is final: no status, priority or detail changes.</summary>
        public static bool IsClosed(IncidentStatus status) => status == IncidentStatus.Closed;
    }
}
