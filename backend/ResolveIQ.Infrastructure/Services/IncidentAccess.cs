using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>
    /// The one place that decides which incidents a user may see. Every service that touches
    /// incidents uses it, so the rule cannot drift apart between features.
    /// </summary>
    internal static class IncidentAccess
    {
        public static bool IsManagerOrAdmin(IReadOnlyCollection<string> roles) =>
            roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Manager);

        /// <summary>
        /// Admins and Managers see everything. Everyone else sees only incidents they
        /// reported or are assigned to.
        /// </summary>
        public static IQueryable<Incident> ApplyVisibility(
            IQueryable<Incident> query,
            string userId,
            IReadOnlyCollection<string> roles)
        {
            return IsManagerOrAdmin(roles)
                ? query
                : query.Where(i => i.ReportedByUserId == userId || i.AssignedToUserId == userId);
        }
    }
}
