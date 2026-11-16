using ResolveIQ.Application.DTOs.Dashboard;

namespace ResolveIQ.Application.Abstractions
{
    public interface IDashboardService
    {
        /// <summary>
        /// The "what needs attention now" numbers, over the incidents this user may see.
        /// </summary>
        Task<DashboardSummaryResponse> GetSummaryAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The "how are we doing" numbers for the last <paramref name="days"/> days, over the incidents
        /// this user may see. The workload section is only filled in for Managers and Admins.
        /// </summary>
        Task<DashboardStatisticsResponse> GetStatisticsAsync(
            int days,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);
    }
}
