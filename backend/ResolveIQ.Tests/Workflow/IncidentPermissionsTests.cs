using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Tests.Workflow
{
    public class IncidentPermissionsTests
    {
        private const string Reporter = "reporter";
        private const string Assignee = "assignee";
        private const string Manager = "manager";
        private const string Stranger = "stranger";

        private static IncidentDetailResponse Incident(IncidentStatus status, bool assigned = true) => new()
        {
            Status = status,
            ReportedByUserId = Reporter,
            AssignedToUserId = assigned ? Assignee : null
        };

        private static IncidentActionsResponse For(IncidentDetailResponse incident, string user, string role) =>
            IncidentPermissions.For(incident, user, new[] { role });

        [Fact]
        public void AManager_CanAssignAndReprioritise_AnOpenIncident_AndStartWorkOnceItHasAnAssignee()
        {
            var actions = For(Incident(IncidentStatus.Open), Manager, RoleNames.Manager);

            Assert.True(actions.CanAssign);
            Assert.True(actions.CanChangePriority);
            Assert.Equal(new[] { IncidentStatus.InProgress }, actions.AllowedStatusChanges);
        }

        [Fact]
        public void NobodyCanStartWork_OnAnIncidentThatHasNoAssigneeYet()
        {
            var actions = For(Incident(IncidentStatus.Open, assigned: false), Manager, RoleNames.Manager);

            Assert.Empty(actions.AllowedStatusChanges);
            Assert.True(actions.CanAssign); // so the manager can fix that
        }

        [Fact]
        public void TheAssignee_DoesTheWork_ButCannotAssignOrChangePriority()
        {
            var inProgress = For(Incident(IncidentStatus.InProgress), Assignee, RoleNames.Employee);

            Assert.Equal(new[] { IncidentStatus.OnHold, IncidentStatus.Resolved }, inProgress.AllowedStatusChanges);
            Assert.False(inProgress.CanAssign);
            Assert.False(inProgress.CanChangePriority);
        }

        [Fact]
        public void TheAssignee_CannotCloseOrReopen_ThatIsTheReportersCall()
        {
            var actions = For(Incident(IncidentStatus.Resolved), Assignee, RoleNames.Employee);

            Assert.Empty(actions.AllowedStatusChanges);
        }

        [Fact]
        public void TheReporter_CanConfirmOrReject_AResolvedIncident()
        {
            var actions = For(Incident(IncidentStatus.Resolved), Reporter, RoleNames.Employee);

            Assert.Equal(
                new[] { IncidentStatus.InProgress, IncidentStatus.Closed },
                actions.AllowedStatusChanges.OrderBy(s => s));
        }

        [Fact]
        public void TheReporter_CannotDoTheWork()
        {
            var actions = For(Incident(IncidentStatus.InProgress), Reporter, RoleNames.Employee);

            Assert.Empty(actions.AllowedStatusChanges);
        }

        [Fact]
        public void SomeoneUninvolved_CanDoNothing()
        {
            foreach (var status in Enum.GetValues<IncidentStatus>())
            {
                var actions = For(Incident(status), Stranger, RoleNames.Employee);

                Assert.Empty(actions.AllowedStatusChanges);
                Assert.False(actions.CanAssign);
                Assert.False(actions.CanChangePriority);
            }
        }

        [Fact]
        public void APriorityIsLocked_OnceResolved_ButAssigningStillWorks()
        {
            var actions = For(Incident(IncidentStatus.Resolved), Manager, RoleNames.Manager);

            Assert.False(actions.CanChangePriority);
            Assert.True(actions.CanAssign);
        }

        [Fact]
        public void AClosedIncident_IsFinal_ForEveryone()
        {
            foreach (var role in new[] { RoleNames.Admin, RoleNames.Manager, RoleNames.Employee })
            {
                var actions = For(Incident(IncidentStatus.Closed), Manager, role);

                Assert.Empty(actions.AllowedStatusChanges);
                Assert.False(actions.CanAssign);
                Assert.False(actions.CanChangePriority);
            }
        }

        [Fact]
        public void AnAdmin_HasTheSamePowersAsAManager()
        {
            var admin = For(Incident(IncidentStatus.InProgress), "admin", RoleNames.Admin);
            var manager = For(Incident(IncidentStatus.InProgress), Manager, RoleNames.Manager);

            Assert.Equal(manager.AllowedStatusChanges, admin.AllowedStatusChanges);
            Assert.Equal(manager.CanAssign, admin.CanAssign);
            Assert.Equal(manager.CanChangePriority, admin.CanChangePriority);
        }
    }
}
