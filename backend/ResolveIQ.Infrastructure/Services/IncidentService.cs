using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class IncidentService : IIncidentService
    {
        // Constant text (no user input), so it is safe to run as raw SQL.
        private const string NextIncidentNumberSql =
            "SELECT NEXT VALUE FOR " + ApplicationDbContext.IncidentNumberSequence + " AS [Value]";

        private static readonly Expression<Func<Incident, IncidentDetailResponse>> ToDetail = i =>
            new IncidentDetailResponse
            {
                Id = i.Id,
                IncidentNumber = i.IncidentNumber,
                Title = i.Title,
                Description = i.Description,
                Priority = i.Priority,
                Status = i.Status,
                CategoryId = i.CategoryId,
                CategoryName = i.Category != null ? i.Category.Name : null,
                ReportedByUserId = i.ReportedByUserId,
                AssignedToUserId = i.AssignedToUserId,
                CreatedAtUtc = i.CreatedAtUtc,
                UpdatedAtUtc = i.UpdatedAtUtc,
                ResolvedAtUtc = i.ResolvedAtUtc
            };

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
            return await ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
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

        public async Task<IncidentDetailResponse?> GetIncidentByIdAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            return await ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .Where(i => i.Id == id)
                .Select(ToDetail)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IncidentDetailResponse?> CreateIncidentAsync(
            string reportedByUserId,
            CreateIncidentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.CategoryId is Guid categoryId)
            {
                var categoryIsUsable = await _db.Categories
                    .AnyAsync(c => c.Id == categoryId && c.IsActive, cancellationToken);
                if (!categoryIsUsable)
                {
                    return null;
                }
            }

            // Ask the database for the next counter value. ToListAsync is used because this
            // statement cannot be wrapped in a subquery the way SingleAsync would require.
            var numbers = await _db.Database
                .SqlQueryRaw<long>(NextIncidentNumberSql)
                .ToListAsync(cancellationToken);

            var incident = new Incident
            {
                IncidentNumber = $"INC-{numbers[0]:D6}",
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                Priority = request.Priority,
                Status = IncidentStatus.Open,
                CategoryId = request.CategoryId,
                ReportedByUserId = reportedByUserId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Incidents.Add(incident);
            await _db.SaveChangesAsync(cancellationToken);

            return await _db.Incidents.AsNoTracking()
                .Where(i => i.Id == incident.Id)
                .Select(ToDetail)
                .SingleAsync(cancellationToken);
        }

        // The visibility rule lives in one place so list and single-item reads always agree.
        private static IQueryable<Incident> ApplyVisibility(
            IQueryable<Incident> query,
            string userId,
            IReadOnlyCollection<string> roles)
        {
            var canViewAll = roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Manager);

            return canViewAll
                ? query
                : query.Where(i => i.ReportedByUserId == userId || i.AssignedToUserId == userId);
        }
    }
}
