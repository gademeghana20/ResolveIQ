using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;

namespace ResolveIQ.Tests.Sla
{
    public class SlaCalculatorTests
    {
        // Monday 2026-11-16 11:00 UTC: the moment the clock starts in most tests.
        private static readonly DateTime Start = new(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc);

        private static Incident Running(DateTime? start = null, long pausedSeconds = 0) => new()
        {
            Status = IncidentStatus.InProgress,
            Priority = IncidentPriority.High,
            SlaStartedAtUtc = start ?? Start,
            SlaPausedSeconds = pausedSeconds
        };

        // The hours an incident was promised live on the incident itself (set when its clock started).
        private static SlaResult Evaluate(Incident incident, int? hours, DateTime now)
        {
            incident.SlaTargetHours = hours;
            return SlaCalculator.Evaluate(incident, now);
        }

        [Fact]
        public void UnassignedIncidentHasNotStarted()
        {
            var incident = new Incident { Status = IncidentStatus.Open };

            var result = Evaluate(incident, 8, Start);

            Assert.Equal(SlaState.NotStarted, result.State);
            Assert.Null(result.DueAtUtc);
        }

        [Fact]
        public void NoTargetMeansNoSla()
        {
            var result = Evaluate(Running(), null, Start.AddHours(1));

            Assert.Equal(SlaState.None, result.State);
            Assert.Null(result.DueAtUtc);
        }

        [Fact]
        public void DeadlineIsStartPlusTarget()
        {
            var result = Evaluate(Running(), 8, Start.AddHours(1));

            Assert.Equal(Start.AddHours(8), result.DueAtUtc);
            Assert.Equal(Start, result.StartedAtUtc);
        }

        [Fact]
        public void PlentyOfTimeLeftIsOnTrack()
        {
            Assert.Equal(SlaState.OnTrack, Evaluate(Running(), 8, Start.AddHours(1)).State);
        }

        [Fact]
        public void ExactlyAQuarterLeftIsAtRisk()
        {
            // 8 h target, 2 h left = exactly 25%.
            Assert.Equal(SlaState.AtRisk, Evaluate(Running(), 8, Start.AddHours(6)).State);
        }

        [Fact]
        public void JustMoreThanAQuarterLeftIsStillOnTrack()
        {
            Assert.Equal(SlaState.OnTrack, Evaluate(Running(), 8, Start.AddHours(6).AddMinutes(-1)).State);
        }

        [Fact]
        public void ExactlyAtTheDeadlineIsNotYetBreached()
        {
            Assert.Equal(SlaState.AtRisk, Evaluate(Running(), 8, Start.AddHours(8)).State);
        }

        [Fact]
        public void OneSecondAfterTheDeadlineIsBreached()
        {
            Assert.Equal(SlaState.Breached, Evaluate(Running(), 8, Start.AddHours(8).AddSeconds(1)).State);
        }

        [Fact]
        public void PausedTimePushesTheDeadlineOut()
        {
            // 5 h spent on hold earlier: deadline is 8 h + 5 h after the start.
            var incident = Running(pausedSeconds: 5 * 3600);

            var result = Evaluate(incident, 8, Start.AddHours(10));

            Assert.Equal(Start.AddHours(13), result.DueAtUtc);
            Assert.Equal(SlaState.OnTrack, result.State);
        }

        [Fact]
        public void WhileOnHoldTheClockIsPausedAndThereIsNoDeadline()
        {
            var incident = Running();
            incident.Status = IncidentStatus.OnHold;
            incident.SlaPausedAtUtc = Start.AddHours(3);

            // Even 100 hours later, it is still just paused.
            var result = Evaluate(incident, 8, Start.AddHours(100));

            Assert.Equal(SlaState.Paused, result.State);
            Assert.Null(result.DueAtUtc);
        }

        [Fact]
        public void AnIncidentAlreadyLateWhenPutOnHoldStaysBreached()
        {
            var incident = Running();
            incident.Status = IncidentStatus.OnHold;
            incident.SlaPausedAtUtc = Start.AddHours(9); // deadline was Start + 8 h

            var result = Evaluate(incident, 8, Start.AddHours(12));

            Assert.Equal(SlaState.Breached, result.State);
        }

        [Fact]
        public void ResolvedBeforeTheDeadlineIsMet()
        {
            var incident = Running();
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAtUtc = Start.AddHours(5);

            // "Now" is long after, but the result is fixed at resolution.
            var result = Evaluate(incident, 8, Start.AddDays(30));

            Assert.Equal(SlaState.Met, result.State);
        }

        [Fact]
        public void ResolvedExactlyAtTheDeadlineIsMet()
        {
            var incident = Running();
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAtUtc = Start.AddHours(8);

            Assert.Equal(SlaState.Met, Evaluate(incident, 8, Start.AddDays(1)).State);
        }

        [Fact]
        public void ResolvedAfterTheDeadlineIsBreached()
        {
            var incident = Running();
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAtUtc = Start.AddHours(8).AddMinutes(1);

            Assert.Equal(SlaState.Breached, Evaluate(incident, 8, Start.AddDays(1)).State);
        }

        [Fact]
        public void ResolvedLateButOnTimeOnceHoldTimeIsExcludedIsMet()
        {
            var incident = Running(pausedSeconds: 6 * 3600);
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAtUtc = Start.AddHours(12); // 12 h elapsed, but 6 h were on hold

            Assert.Equal(SlaState.Met, Evaluate(incident, 8, Start.AddDays(1)).State);
        }

        [Fact]
        public void ClosedKeepsItsResult()
        {
            var late = Running();
            late.Status = IncidentStatus.Closed;
            late.ResolvedAtUtc = Start.AddHours(9);

            var onTime = Running();
            onTime.Status = IncidentStatus.Closed;
            onTime.ResolvedAtUtc = Start.AddHours(2);

            Assert.Equal(SlaState.Breached, Evaluate(late, 8, Start.AddDays(5)).State);
            Assert.Equal(SlaState.Met, Evaluate(onTime, 8, Start.AddDays(5)).State);
        }

        [Fact]
        public void TheFullWorkedExample_FromAssignmentToReopen()
        {
            // Mon 11:00  assigned, High (8 h): deadline 19:00.
            var incident = new Incident
            {
                Status = IncidentStatus.InProgress,
                Priority = IncidentPriority.High,
                SlaStartedAtUtc = Start
            };
            Assert.Equal(Start.AddHours(8), Evaluate(incident, 8, Start.AddHours(3)).DueAtUtc);

            // Mon 14:00  put on hold; Tue 10:00 resumed (20 h paused): deadline moves to Tue 15:00.
            incident.SlaPausedSeconds = 20 * 3600;
            var resumed = Evaluate(incident, 8, Start.AddHours(23));
            Assert.Equal(new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc), resumed.DueAtUtc);

            // Tue 12:00  raised to Critical (4 h): the clock restarts, deadline Tue 16:00.
            var restartAt = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
            incident.Priority = IncidentPriority.Critical;
            incident.SlaStartedAtUtc = restartAt;
            incident.SlaPausedSeconds = 0;
            Assert.Equal(restartAt.AddHours(4), Evaluate(incident, 4, restartAt).DueAtUtc);

            // Tue 15:00  resolved on time.
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAtUtc = restartAt.AddHours(3);
            Assert.Equal(SlaState.Met, Evaluate(incident, 4, restartAt.AddHours(3)).State);

            // Tue 17:00  reopened: the 2 h spent Resolved are added to the paused time.
            incident.Status = IncidentStatus.InProgress;
            incident.SlaPausedSeconds += (long)(restartAt.AddHours(5) - incident.ResolvedAtUtc.Value).TotalSeconds;
            incident.ResolvedAtUtc = null;

            var reopened = Evaluate(incident, 4, restartAt.AddHours(5));
            Assert.Equal(restartAt.AddHours(6), reopened.DueAtUtc); // Tue 18:00
            Assert.Equal(SlaState.AtRisk, reopened.State);          // 1 h left of 4 h = 25%
        }
    }
}
