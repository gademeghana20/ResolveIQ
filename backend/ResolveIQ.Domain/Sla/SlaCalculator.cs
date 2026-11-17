using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Domain.Sla
{
    public enum SlaState
    {
        /// <summary>The incident has no SLA target (no active policy applied when its clock started).</summary>
        None,

        /// <summary>The incident has not been assigned yet, so the clock has not started.</summary>
        NotStarted,

        OnTrack,

        /// <summary>Running, with 25% or less of the target time left.</summary>
        AtRisk,

        /// <summary>The clock is paused because the incident is OnHold.</summary>
        Paused,

        Breached,

        /// <summary>Resolved on or before the deadline.</summary>
        Met
    }

    public record SlaResult(SlaState State, DateTime? StartedAtUtc, DateTime? DueAtUtc);

    /// <summary>
    /// Works out an incident's SLA deadline and state. Pure logic: the current time is passed in,
    /// so tests can say exactly what time it is.
    /// </summary>
    public static class SlaCalculator
    {
        /// <summary>The "at risk" warning starts when this fraction of the target is left.</summary>
        public const double AtRiskFraction = 0.25;

        /// <summary>
        /// Works out the state from what the incident itself remembers, including the hours it was promised.
        /// It deliberately does not look at today's SLA policy: that only decides the hours of incidents
        /// whose clock starts later.
        /// </summary>
        public static SlaResult Evaluate(Incident incident, DateTime nowUtc)
        {
            if (incident.SlaStartedAtUtc is not DateTime started)
            {
                return new SlaResult(SlaState.NotStarted, null, null);
            }

            if (incident.SlaTargetHours is not int hours)
            {
                return new SlaResult(SlaState.None, started, null);
            }

            var target = TimeSpan.FromHours(hours);

            // Paused time (OnHold, or Resolved before a reopen) does not count, so it pushes the deadline out.
            var due = started + target + TimeSpan.FromSeconds(incident.SlaPausedSeconds);

            // Resolved or closed: the clock has stopped, so the result is fixed.
            if (incident.ResolvedAtUtc is DateTime resolvedAt
                && incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed)
            {
                var state = resolvedAt <= due ? SlaState.Met : SlaState.Breached;
                return new SlaResult(state, started, due);
            }

            // On hold: the clock is paused. If it was already late when it paused, it stays Breached.
            if (incident.SlaPausedAtUtc is DateTime pausedAt)
            {
                return pausedAt > due
                    ? new SlaResult(SlaState.Breached, started, due)
                    : new SlaResult(SlaState.Paused, started, null);
            }

            if (nowUtc > due)
            {
                return new SlaResult(SlaState.Breached, started, due);
            }

            var remaining = due - nowUtc;
            var state2 = remaining <= target * AtRiskFraction ? SlaState.AtRisk : SlaState.OnTrack;
            return new SlaResult(state2, started, due);
        }
    }
}
