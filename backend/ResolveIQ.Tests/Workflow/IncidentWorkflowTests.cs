using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Workflow;

namespace ResolveIQ.Tests.Workflow
{
    public class IncidentWorkflowTests
    {
        private static readonly IncidentStatus[] AllStatuses = Enum.GetValues<IncidentStatus>();

        private static readonly (IncidentStatus From, IncidentStatus To)[] ValidTransitions =
        {
            (IncidentStatus.Open, IncidentStatus.InProgress),
            (IncidentStatus.InProgress, IncidentStatus.OnHold),
            (IncidentStatus.InProgress, IncidentStatus.Resolved),
            (IncidentStatus.OnHold, IncidentStatus.InProgress),
            (IncidentStatus.Resolved, IncidentStatus.Closed),
            (IncidentStatus.Resolved, IncidentStatus.InProgress)
        };

        [Fact]
        public void ExactlyTheSixValidTransitionsAreAllowed()
        {
            var allowed = new List<(IncidentStatus, IncidentStatus)>();

            foreach (var from in AllStatuses)
            {
                foreach (var to in AllStatuses)
                {
                    if (IncidentWorkflow.IsTransitionAllowed(from, to))
                    {
                        allowed.Add((from, to));
                    }
                }
            }

            Assert.Equal(ValidTransitions.Length, allowed.Count);
            Assert.All(ValidTransitions, t => Assert.Contains(t, allowed));
        }

        [Fact]
        public void ClosedIsFinal_NobodyCanMoveOutOfIt_NotEvenAManager()
        {
            foreach (var to in AllStatuses)
            {
                var result = IncidentWorkflow.CheckStatusChange(
                    IncidentStatus.Closed, to,
                    isManagerOrAdmin: true, isAssignee: true, isReporter: true, hasAssignee: true);

                Assert.Equal(StatusChangeCheck.TransitionNotAllowed, result);
            }
        }

        [Fact]
        public void StayingInTheSameStatusIsNotATransition()
        {
            foreach (var status in AllStatuses)
            {
                var result = IncidentWorkflow.CheckStatusChange(
                    status, status,
                    isManagerOrAdmin: true, isAssignee: true, isReporter: true, hasAssignee: true);

                Assert.Equal(StatusChangeCheck.TransitionNotAllowed, result);
            }
        }

        [Theory]
        [InlineData(IncidentStatus.Open, IncidentStatus.InProgress)]
        [InlineData(IncidentStatus.InProgress, IncidentStatus.OnHold)]
        [InlineData(IncidentStatus.OnHold, IncidentStatus.InProgress)]
        [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved)]
        public void AssigneeMayDoTheWorkMoves(IncidentStatus from, IncidentStatus to)
        {
            var result = IncidentWorkflow.CheckStatusChange(
                from, to, isManagerOrAdmin: false, isAssignee: true, isReporter: false, hasAssignee: true);

            Assert.Equal(StatusChangeCheck.Allowed, result);
        }

        [Theory]
        [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed)]
        [InlineData(IncidentStatus.Resolved, IncidentStatus.InProgress)]
        public void AssigneeMayNotCloseOrReopen(IncidentStatus from, IncidentStatus to)
        {
            var result = IncidentWorkflow.CheckStatusChange(
                from, to, isManagerOrAdmin: false, isAssignee: true, isReporter: false, hasAssignee: true);

            Assert.Equal(StatusChangeCheck.NotPermitted, result);
        }

        [Theory]
        [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed)]
        [InlineData(IncidentStatus.Resolved, IncidentStatus.InProgress)]
        public void ReporterMayConfirmOrReopenAResolvedIncident(IncidentStatus from, IncidentStatus to)
        {
            var result = IncidentWorkflow.CheckStatusChange(
                from, to, isManagerOrAdmin: false, isAssignee: false, isReporter: true, hasAssignee: true);

            Assert.Equal(StatusChangeCheck.Allowed, result);
        }

        [Theory]
        [InlineData(IncidentStatus.Open, IncidentStatus.InProgress)]
        [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved)]
        [InlineData(IncidentStatus.InProgress, IncidentStatus.OnHold)]
        public void ReporterMayNotDoTheWork(IncidentStatus from, IncidentStatus to)
        {
            var result = IncidentWorkflow.CheckStatusChange(
                from, to, isManagerOrAdmin: false, isAssignee: false, isReporter: true, hasAssignee: true);

            Assert.Equal(StatusChangeCheck.NotPermitted, result);
        }

        [Fact]
        public void AnUninvolvedEmployeeMayDoNothing()
        {
            foreach (var (from, to) in ValidTransitions)
            {
                var result = IncidentWorkflow.CheckStatusChange(
                    from, to, isManagerOrAdmin: false, isAssignee: false, isReporter: false, hasAssignee: true);

                Assert.Equal(StatusChangeCheck.NotPermitted, result);
            }
        }

        [Fact]
        public void ManagersAndAdminsMayMakeAnyValidTransition()
        {
            foreach (var (from, to) in ValidTransitions)
            {
                var result = IncidentWorkflow.CheckStatusChange(
                    from, to, isManagerOrAdmin: true, isAssignee: false, isReporter: false, hasAssignee: true);

                Assert.Equal(StatusChangeCheck.Allowed, result);
            }
        }

        [Fact]
        public void WorkCannotStartOnAnUnassignedIncident_EvenForAManager()
        {
            var result = IncidentWorkflow.CheckStatusChange(
                IncidentStatus.Open, IncidentStatus.InProgress,
                isManagerOrAdmin: true, isAssignee: false, isReporter: false, hasAssignee: false);

            Assert.Equal(StatusChangeCheck.AssigneeRequired, result);
        }

        [Fact]
        public void InvalidTransitionIsReportedBeforeMissingAssignee()
        {
            var result = IncidentWorkflow.CheckStatusChange(
                IncidentStatus.Open, IncidentStatus.Closed,
                isManagerOrAdmin: true, isAssignee: false, isReporter: false, hasAssignee: false);

            Assert.Equal(StatusChangeCheck.TransitionNotAllowed, result);
        }

        [Fact]
        public void OnlyClosedIsFinal()
        {
            foreach (var status in AllStatuses)
            {
                Assert.Equal(status == IncidentStatus.Closed, IncidentWorkflow.IsClosed(status));
            }
        }
    }
}
