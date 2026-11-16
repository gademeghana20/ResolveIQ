using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class IncidentService : IIncidentService
    {
        private readonly ApplicationDbContext _db;

        public IncidentService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<IncidentSummaryResponse>> GetIncidentsAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var query = _db.Incidents.AsNoTracking();

            var canViewAll = roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Manager);
            if (!canViewAll)
            {
                query = query.Where(i => i.ReportedByUserId == userId || i.AssignedToUserId == userId);
            }

            return await query
                .OrderByDescending(i => i.CreatedAtUtc)
                .Select(i => new IncidentSummaryResponse
                {
                    Id = i.Id,
                    IncidentNumber = i.IncidentNumber,
                    Title = i.Title,
                    Priority = i.Priority,
                    Status = i.Status,
                    CategoryName = i.Category != null ? i.Category.Name : null,
                    ReportedByUserId = i.ReportedByUserId,
                    AssignedToUserId = i.AssignedToUserId,
                    CreatedAtUtc = i.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);
        }
    }
}
