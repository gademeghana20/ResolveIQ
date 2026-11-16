using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Workflow;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class IncidentService : IIncidentService
    {
        // Constant text (no user input), so it is safe to run as raw SQL.
        private const string NextIncidentNumberSql =
            "SELECT NEXT VALUE FOR " + ApplicationDbContext.IncidentNumberSequence + " AS [Value]";

        // History columns hold 500 characters.
        private const int HistoryValueMaxLength = 500;

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

            return await LoadDetailAsync(incident.Id, cancellationToken);
        }

        public async Task<IncidentChangeResult> AssignIncidentAsync(
            Guid id,
            string assigneeUserId,
            string actingUserId,
            CancellationToken cancellationToken = default)
        {
            var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (incident is null)
            {
                return new IncidentChangeResult(ChangeOutcome.NotFound);
            }

            var assignee = await _db.Users.AsNoTracking()
                .Where(u => u.Id == assigneeUserId)
                .Select(u => new { u.Id, u.Email })
                .FirstOrDefaultAsync(cancellationToken);
            if (assignee is null)
            {
                return Invalid("AssigneeUserId", "No user exists with that id.");
            }

            // Assigning to the person who already has it changes nothing, so write no history.
            if (incident.AssignedToUserId != assignee.Id)
            {
                // History shows emails (readable) rather than ids.
                string? previousAssignee = incident.AssignedToUserId is null
                    ? null
                    : await _db.Users.AsNoTracking()
                        .Where(u => u.Id == incident.AssignedToUserId)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync(cancellationToken);

                incident.AssignedToUserId = assignee.Id;
                incident.UpdatedAtUtc = DateTime.UtcNow;
                AddHistory(incident.Id, actingUserId, "AssignedTo", previousAssignee, assignee.Email);

                if (!await TrySaveAsync(cancellationToken))
                {
                    return new IncidentChangeResult(ChangeOutcome.Conflict);
                }
            }

            return await SuccessAsync(id, cancellationToken);
        }

        public async Task<IncidentChangeResult> ChangeStatusAsync(
            Guid id,
            IncidentStatus newStatus,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var incident = await FindVisibleTrackedAsync(id, userId, roles, cancellationToken);
            if (incident is null)
            {
                return new IncidentChangeResult(ChangeOutcome.NotFound);
            }

            var check = IncidentWorkflow.CheckStatusChange(
                from: incident.Status,
                to: newStatus,
                isManagerOrAdmin: IsManagerOrAdmin(roles),
                isAssignee: incident.AssignedToUserId == userId,
                isReporter: incident.ReportedByUserId == userId,
                hasAssignee: incident.AssignedToUserId is not null);

            switch (check)
            {
                case StatusChangeCheck.TransitionNotAllowed:
                    return Invalid("Status", $"An incident cannot move from {incident.Status} to {newStatus}.");
                case StatusChangeCheck.AssigneeRequired:
                    return Invalid("Status", "Assign the incident to someone before work can start.");
                case StatusChangeCheck.NotPermitted:
                    return new IncidentChangeResult(ChangeOutcome.Forbidden);
            }

            var oldStatus = incident.Status;
            var now = DateTime.UtcNow;

            incident.Status = newStatus;
            incident.UpdatedAtUtc = now;

            if (newStatus == IncidentStatus.Resolved)
            {
                incident.ResolvedAtUtc = now;
            }
            else if (oldStatus == IncidentStatus.Resolved && newStatus == IncidentStatus.InProgress)
            {
                incident.ResolvedAtUtc = null; // reopened
            }

            AddHistory(incident.Id, userId, "Status", oldStatus.ToString(), newStatus.ToString());

            return await SaveAndRespondAsync(id, cancellationToken);
        }

        public async Task<IncidentChangeResult> ChangePriorityAsync(
            Guid id,
            IncidentPriority newPriority,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var incident = await FindVisibleTrackedAsync(id, userId, roles, cancellationToken);
            if (incident is null)
            {
                return new IncidentChangeResult(ChangeOutcome.NotFound);
            }

            if (!IsManagerOrAdmin(roles))
            {
                return new IncidentChangeResult(ChangeOutcome.Forbidden);
            }

            if (IncidentWorkflow.IsClosed(incident.Status))
            {
                return Invalid("Priority", "The priority of a closed incident cannot be changed.");
            }

            if (incident.Priority == newPriority)
            {
                return await SuccessAsync(id, cancellationToken);
            }

            var oldPriority = incident.Priority;
            incident.Priority = newPriority;
            incident.UpdatedAtUtc = DateTime.UtcNow;
            AddHistory(incident.Id, userId, "Priority", oldPriority.ToString(), newPriority.ToString());

            return await SaveAndRespondAsync(id, cancellationToken);
        }

        public async Task<IncidentChangeResult> UpdateIncidentAsync(
            Guid id,
            UpdateIncidentRequest request,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var incident = await FindVisibleTrackedAsync(id, userId, roles, cancellationToken);
            if (incident is null)
            {
                return new IncidentChangeResult(ChangeOutcome.NotFound);
            }

            if (!IsManagerOrAdmin(roles) && incident.ReportedByUserId != userId)
            {
                return new IncidentChangeResult(ChangeOutcome.Forbidden);
            }

            if (IncidentWorkflow.IsClosed(incident.Status))
            {
                return Invalid("Status", "A closed incident cannot be edited.");
            }

            var newTitle = request.Title.Trim();
            var newDescription = request.Description.Trim();
            var changed = false;

            if (incident.Title != newTitle)
            {
                AddHistory(incident.Id, userId, "Title", incident.Title, newTitle);
                incident.Title = newTitle;
                changed = true;
            }

            if (incident.Description != newDescription)
            {
                AddHistory(incident.Id, userId, "Description", incident.Description, newDescription);
                incident.Description = newDescription;
                changed = true;
            }

            if (!changed)
            {
                return await SuccessAsync(id, cancellationToken);
            }

            incident.UpdatedAtUtc = DateTime.UtcNow;
            return await SaveAndRespondAsync(id, cancellationToken);
        }

        public async Task<IReadOnlyList<IncidentHistoryResponse>?> GetHistoryAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var canSee = await ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles)
                .AnyAsync(i => i.Id == id, cancellationToken);
            if (!canSee)
            {
                return null;
            }

            return await _db.IncidentHistory.AsNoTracking()
                .Where(h => h.IncidentId == id)
                .OrderBy(h => h.ChangedAtUtc)
                .Select(h => new IncidentHistoryResponse
                {
                    FieldChanged = h.FieldChanged,
                    OldValue = h.OldValue,
                    NewValue = h.NewValue,
                    ChangedBy = _db.Users
                        .Where(u => u.Id == h.ChangedByUserId)
                        .Select(u => u.Email)
                        .FirstOrDefault(),
                    ChangedAtUtc = h.ChangedAtUtc
                })
                .ToListAsync(cancellationToken);
        }

        // ---- helpers ----

        // The visibility rule lives in one place so list, single-item reads and changes always agree.
        private static IQueryable<Incident> ApplyVisibility(
            IQueryable<Incident> query,
            string userId,
            IReadOnlyCollection<string> roles)
        {
            return IsManagerOrAdmin(roles)
                ? query
                : query.Where(i => i.ReportedByUserId == userId || i.AssignedToUserId == userId);
        }

        private static bool IsManagerOrAdmin(IReadOnlyCollection<string> roles) =>
            roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Manager);

        // Loads the incident for editing (tracked by EF), but only if this user may see it.
        private Task<Incident?> FindVisibleTrackedAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) =>
            ApplyVisibility(_db.Incidents, userId, roles).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        private void AddHistory(Guid incidentId, string changedByUserId, string field, string? oldValue, string? newValue)
        {
            _db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = incidentId,
                ChangedByUserId = changedByUserId,
                FieldChanged = field,
                OldValue = Truncate(oldValue),
                NewValue = Truncate(newValue),
                ChangedAtUtc = DateTime.UtcNow
            });
        }

        private static string? Truncate(string? value) =>
            value is { Length: > HistoryValueMaxLength } ? value[..(HistoryValueMaxLength - 1)] + "…" : value;

        // One SaveChanges = one transaction: the change and its history rows are saved together or not at all.
        private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        private async Task<IncidentChangeResult> SaveAndRespondAsync(Guid id, CancellationToken cancellationToken) =>
            await TrySaveAsync(cancellationToken)
                ? await SuccessAsync(id, cancellationToken)
                : new IncidentChangeResult(ChangeOutcome.Conflict);

        private async Task<IncidentChangeResult> SuccessAsync(Guid id, CancellationToken cancellationToken) =>
            new(ChangeOutcome.Success, await LoadDetailAsync(id, cancellationToken));

        private Task<IncidentDetailResponse> LoadDetailAsync(Guid id, CancellationToken cancellationToken) =>
            _db.Incidents.AsNoTracking()
                .Where(i => i.Id == id)
                .Select(ToDetail)
                .SingleAsync(cancellationToken);

        private static IncidentChangeResult Invalid(string field, string message) =>
            new(ChangeOutcome.Invalid, Field: field, Message: message);
    }
}
