using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.Dashboard;
using ResolveIQ.Application.DTOs.Dashboard;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;

        public DashboardService(ApplicationDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public async Task<DashboardSummaryResponse> GetSummaryAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var incidents = await LoadVisibleIncidentsAsync(userId, roles, cancellationToken);

            return DashboardCalculator.BuildSummary(incidents, userId, Now());
        }

        public async Task<DashboardStatisticsResponse> GetStatisticsAsync(
            int days,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var incidents = await LoadVisibleIncidentsAsync(userId, roles, cancellationToken);

            var categoryNames = await _db.Categories.AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

            // Only Managers and Admins get the per-person workload table.
            var includeWorkload = IncidentAccess.IsManagerOrAdmin(roles);
            var userEmails = new Dictionary<string, string>();

            if (includeWorkload)
            {
                var assigneeIds = incidents
                    .Where(i => i.AssignedToUserId is not null)
                    .Select(i => i.AssignedToUserId!)
                    .Distinct()
                    .ToList();

                userEmails = await _db.Users.AsNoTracking()
                    .Where(u => assigneeIds.Contains(u.Id) && u.Email != null)
                    .ToDictionaryAsync(u => u.Id, u => u.Email!, cancellationToken);
            }

            return DashboardCalculator.BuildStatistics(
                incidents, categoryNames, userEmails, includeWorkload, days, Now());
        }

        private DateTime Now() => _time.GetUtcNow().UtcDateTime;

        // Loads only the columns the numbers need (not titles or descriptions), for the incidents this user may see.
        private async Task<List<Incident>> LoadVisibleIncidentsAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            var rows = await IncidentAccess.ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .Select(i => new
                {
                    i.Id,
                    i.Status,
                    i.Priority,
                    i.CategoryId,
                    i.AssignedToUserId,
                    i.CreatedAtUtc,
                    i.ResolvedAtUtc,
                    i.SlaStartedAtUtc,
                    i.SlaTargetHours,
                    i.SlaPausedAtUtc,
                    i.SlaPausedSeconds
                })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new Incident
            {
                Id = r.Id,
                Status = r.Status,
                Priority = r.Priority,
                CategoryId = r.CategoryId,
                AssignedToUserId = r.AssignedToUserId,
                CreatedAtUtc = r.CreatedAtUtc,
                ResolvedAtUtc = r.ResolvedAtUtc,
                SlaStartedAtUtc = r.SlaStartedAtUtc,
                SlaTargetHours = r.SlaTargetHours,
                SlaPausedAtUtc = r.SlaPausedAtUtc,
                SlaPausedSeconds = r.SlaPausedSeconds
            }).ToList();
        }
    }
}
