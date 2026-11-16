using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;
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

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;

        public IncidentService(ApplicationDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        // ---- reading ----

        public async Task<PagedResult<IncidentSummaryResponse>> GetIncidentsAsync(
            IncidentQuery query,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            // 1. Only what this user may see. Filters can narrow this, never widen it.
            var incidents = ApplyVisibility(_db.Incidents.AsNoTracking(), userId, roles);

            // 2. The user's filters, all done by SQL Server.
            incidents = ApplyFilters(incidents, query, userId);
            var sorted = ApplySorting(incidents, query.SortBy, query.SortDirection);

            var targets = await LoadTargetHoursAsync(cancellationToken);
            var now = Now();

            // The SLA state is worked out in code, not stored, so SQL cannot filter on it.
            // When it is requested, load the matches so far, filter them here, then page them here.
            if (query.SlaState is SlaState wantedState)
            {
                var matches = (await sorted.Include(i => i.Category).ToListAsync(cancellationToken))
                    .Select(i => ToSummary(i, targets, now))
                    .Where(s => s.SlaState == wantedState)
                    .ToList();

                return new PagedResult<IncidentSummaryResponse>
                {
                    Items = matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
                    Page = query.Page,
                    PageSize = query.PageSize,
                    TotalCount = matches.Count
                };
            }

            // 3. The normal path: count, then fetch only the requested page.
            var totalCount = await sorted.CountAsync(cancellationToken);

            var page = await sorted
                .Include(i => i.Category)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<IncidentSummaryResponse>
            {
                Items = page.Select(i => ToSummary(i, targets, now)).ToList(),
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = totalCount
            };
        }

        public async Task<IncidentDetailResponse?> GetIncidentByIdAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default)
        {
            var incident = await ApplyVisibility(
                    _db.Incidents.AsNoTracking().Include(i => i.Category), userId, roles)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

            return incident is null ? null : ToDetail(incident, await LoadTargetHoursAsync(cancellationToken), Now());
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

        // ---- changing ----

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
                Status = IncidentStatus.Open,
                CategoryId = request.CategoryId,
                ReportedByUserId = reportedByUserId,
                CreatedAtUtc = Now()
            };

            _db.Incidents.Add(incident);
            await _db.SaveChangesAsync(cancellationToken);

            return await LoadDetailAsync(incident.Id, cancellationToken);
        }

        public async Task<IncidentChangeResult> AssignIncidentAsync(
            Guid id,
            string assigneeUserId,
            IncidentPriority? priority,
            string actingUserId,
            CancellationToken cancellationToken = default)
        {
            var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (incident is null)
            {
                return new IncidentChangeResult(ChangeOutcome.NotFound);
            }

            if (IncidentWorkflow.IsClosed(incident.Status))
            {
                return Invalid("Status", "A closed incident cannot be assigned.");
            }

            if (priority is null && incident.Priority is null)
            {
                return Invalid("Priority", "Set a priority when assigning an incident that has none yet.");
            }

            var assignee = await _db.Users.AsNoTracking()
                .Where(u => u.Id == assigneeUserId)
                .Select(u => new { u.Id, u.Email, u.LockoutEnd })
                .FirstOrDefaultAsync(cancellationToken);
            if (assignee is null)
            {
                return Invalid("AssigneeUserId", "No user exists with that id.");
            }

            // A deactivated user cannot log in, so giving them work would leave it stuck.
            if (assignee.LockoutEnd > _time.GetUtcNow())
            {
                return Invalid("AssigneeUserId", "That user is inactive and cannot be assigned incidents.");
            }

            var now = Now();
            var targets = await LoadTargetHoursAsync(cancellationToken);
            var changed = false;

            // Priority first, so the clock below starts with the right target.
            if (priority is IncidentPriority newPriority && newPriority != incident.Priority)
            {
                if (!IncidentWorkflow.CanChangePriority(incident.Status))
                {
                    return Invalid("Priority", PriorityLockedMessage);
                }

                ApplyPriorityChange(incident, newPriority, actingUserId, targets, now);
                changed = true;
            }

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
                AddHistory(incident.Id, actingUserId, "AssignedTo", previousAssignee, assignee.Email, now);
                changed = true;
            }

            // The first assignment starts the SLA clock. Later reassignments do not restart it.
            if (incident.SlaStartedAtUtc is null)
            {
                incident.SlaStartedAtUtc = now;
                AddHistory(incident.Id, actingUserId, "SlaClock", null, "Started", now);
                changed = true;
            }

            if (changed)
            {
                incident.UpdatedAtUtc = now;
                return await SaveAndRespondAsync(id, cancellationToken);
            }

            return await SuccessAsync(id, cancellationToken);
        }

        public async Task<IncidentChangeResult> ChangeStatusAsync(
            Guid id,
            IncidentStatus newStatus,
            string? reason,
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

            var trimmedReason = reason?.Trim();
            if (newStatus == IncidentStatus.OnHold && string.IsNullOrEmpty(trimmedReason))
            {
                return Invalid("Reason", "A reason is required when putting an incident on hold.");
            }

            var oldStatus = incident.Status;
            var now = Now();

            incident.Status = newStatus;
            incident.UpdatedAtUtc = now;

            // SLA clock: time spent on hold, or resolved before a reopen, does not count.
            if (newStatus == IncidentStatus.OnHold)
            {
                incident.SlaPausedAtUtc = now;
            }
            else if (oldStatus == IncidentStatus.OnHold && incident.SlaPausedAtUtc is DateTime pausedAt)
            {
                incident.SlaPausedSeconds += SecondsBetween(pausedAt, now);
                incident.SlaPausedAtUtc = null;
            }

            if (newStatus == IncidentStatus.Resolved)
            {
                incident.ResolvedAtUtc = now;
            }
            else if (oldStatus == IncidentStatus.Resolved && newStatus == IncidentStatus.InProgress)
            {
                if (incident.ResolvedAtUtc is DateTime resolvedAt)
                {
                    incident.SlaPausedSeconds += SecondsBetween(resolvedAt, now);
                }

                incident.ResolvedAtUtc = null; // reopened
            }

            AddHistory(incident.Id, userId, "Status", oldStatus.ToString(), newStatus.ToString(), now);
            if (newStatus == IncidentStatus.OnHold)
            {
                AddHistory(incident.Id, userId, "OnHoldReason", null, trimmedReason, now);
            }

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

            if (!IncidentWorkflow.CanChangePriority(incident.Status))
            {
                return Invalid("Priority", PriorityLockedMessage);
            }

            if (incident.Priority == newPriority)
            {
                return await SuccessAsync(id, cancellationToken);
            }

            var now = Now();
            ApplyPriorityChange(incident, newPriority, userId, await LoadTargetHoursAsync(cancellationToken), now);
            incident.UpdatedAtUtc = now;

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

            var now = Now();
            var newTitle = request.Title.Trim();
            var newDescription = request.Description.Trim();
            var changed = false;

            if (incident.Title != newTitle)
            {
                AddHistory(incident.Id, userId, "Title", incident.Title, newTitle, now);
                incident.Title = newTitle;
                changed = true;
            }

            if (incident.Description != newDescription)
            {
                AddHistory(incident.Id, userId, "Description", incident.Description, newDescription, now);
                incident.Description = newDescription;
                changed = true;
            }

            if (!changed)
            {
                return await SuccessAsync(id, cancellationToken);
            }

            incident.UpdatedAtUtc = now;
            return await SaveAndRespondAsync(id, cancellationToken);
        }

        // ---- helpers ----

        private const string PriorityLockedMessage =
            "The priority cannot be changed once an incident is resolved or closed. Reopen it first.";

        private DateTime Now() => _time.GetUtcNow().UtcDateTime;

        private static long SecondsBetween(DateTime from, DateTime to) =>
            Math.Max(0, (long)(to - from).TotalSeconds);

        // The visibility rule lives in IncidentAccess, shared with the other incident services.
        private static IQueryable<Incident> ApplyVisibility(
            IQueryable<Incident> query,
            string userId,
            IReadOnlyCollection<string> roles) =>
            IncidentAccess.ApplyVisibility(query, userId, roles);

        private static IQueryable<Incident> ApplyFilters(IQueryable<Incident> incidents, IncidentQuery query, string userId)
        {
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var text = query.Search.Trim();
                incidents = incidents.Where(i =>
                    i.IncidentNumber.Contains(text) || i.Title.Contains(text) || i.Description.Contains(text));
            }

            if (query.Status is IncidentStatus status)
            {
                incidents = incidents.Where(i => i.Status == status);
            }

            if (query.Priority is IncidentPriority priority)
            {
                incidents = incidents.Where(i => i.Priority == priority);
            }

            if (query.CategoryId is Guid categoryId)
            {
                incidents = incidents.Where(i => i.CategoryId == categoryId);
            }

            if (!string.IsNullOrWhiteSpace(query.AssignedToUserId))
            {
                var assigneeId = query.AssignedToUserId.Trim();
                incidents = incidents.Where(i => i.AssignedToUserId == assigneeId);
            }

            if (query.Unassigned is true)
            {
                incidents = incidents.Where(i => i.AssignedToUserId == null);
            }

            if (query.AssignedToMe)
            {
                incidents = incidents.Where(i => i.AssignedToUserId == userId);
            }

            if (query.ReportedByMe)
            {
                incidents = incidents.Where(i => i.ReportedByUserId == userId);
            }

            return incidents;
        }

        private static IQueryable<Incident> ApplySorting(
            IQueryable<Incident> incidents,
            IncidentSortField field,
            SortDirection direction)
        {
            var descending = direction == SortDirection.Desc;

            IOrderedQueryable<Incident> sorted = field switch
            {
                IncidentSortField.Priority => descending
                    ? incidents.OrderByDescending(i => i.Priority)
                    : incidents.OrderBy(i => i.Priority),
                IncidentSortField.Status => descending
                    ? incidents.OrderByDescending(i => i.Status)
                    : incidents.OrderBy(i => i.Status),
                IncidentSortField.IncidentNumber => descending
                    ? incidents.OrderByDescending(i => i.IncidentNumber)
                    : incidents.OrderBy(i => i.IncidentNumber),
                IncidentSortField.Title => descending
                    ? incidents.OrderByDescending(i => i.Title)
                    : incidents.OrderBy(i => i.Title),
                _ => descending
                    ? incidents.OrderByDescending(i => i.CreatedAtUtc)
                    : incidents.OrderBy(i => i.CreatedAtUtc)
            };

            // A tie-breaker keeps the order stable, so an incident never appears on two pages.
            return sorted.ThenBy(i => i.Id);
        }

        private static bool IsManagerOrAdmin(IReadOnlyCollection<string> roles) =>
            IncidentAccess.IsManagerOrAdmin(roles);

        // Loads the incident for editing (tracked by EF), but only if this user may see it.
        private Task<Incident?> FindVisibleTrackedAsync(
            Guid id,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) =>
            ApplyVisibility(_db.Incidents, userId, roles).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        // Sets a new priority. If the SLA clock is already running, it restarts with the new target,
        // and the history records what state the old clock was in (so a breach cannot be hidden).
        private void ApplyPriorityChange(
            Incident incident,
            IncidentPriority newPriority,
            string changedByUserId,
            IReadOnlyDictionary<IncidentPriority, int> targets,
            DateTime now)
        {
            var oldPriority = incident.Priority;
            var previousState = SlaCalculator.Evaluate(incident, TargetFor(oldPriority, targets), now).State;

            incident.Priority = newPriority;
            AddHistory(incident.Id, changedByUserId, "Priority", oldPriority?.ToString(), newPriority.ToString(), now);

            if (incident.SlaStartedAtUtc is not null)
            {
                incident.SlaStartedAtUtc = now;
                incident.SlaPausedSeconds = 0;
                incident.SlaPausedAtUtc = incident.Status == IncidentStatus.OnHold ? now : null;
                AddHistory(incident.Id, changedByUserId, "SlaClock", previousState.ToString(), "Restarted", now);
            }
        }

        private void AddHistory(
            Guid incidentId,
            string changedByUserId,
            string field,
            string? oldValue,
            string? newValue,
            DateTime now)
        {
            _db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = incidentId,
                ChangedByUserId = changedByUserId,
                FieldChanged = field,
                OldValue = Truncate(oldValue),
                NewValue = Truncate(newValue),
                ChangedAtUtc = now
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

        private async Task<IncidentDetailResponse> LoadDetailAsync(Guid id, CancellationToken cancellationToken)
        {
            var incident = await _db.Incidents.AsNoTracking()
                .Include(i => i.Category)
                .SingleAsync(i => i.Id == id, cancellationToken);

            return ToDetail(incident, await LoadTargetHoursAsync(cancellationToken), Now());
        }

        private static IncidentChangeResult Invalid(string field, string message) =>
            new(ChangeOutcome.Invalid, Field: field, Message: message);

        // ---- SLA and mapping ----

        // The active resolution targets, by priority. There are only a handful of rows.
        private async Task<Dictionary<IncidentPriority, int>> LoadTargetHoursAsync(CancellationToken cancellationToken) =>
            await _db.SlaPolicies.AsNoTracking()
                .Where(p => p.IsActive)
                .ToDictionaryAsync(p => p.Priority, p => p.ResolutionTargetHours, cancellationToken);

        private static int? TargetFor(IncidentPriority? priority, IReadOnlyDictionary<IncidentPriority, int> targets) =>
            priority is IncidentPriority p && targets.TryGetValue(p, out var hours) ? hours : null;

        private static IncidentSummaryResponse ToSummary(
            Incident i,
            IReadOnlyDictionary<IncidentPriority, int> targets,
            DateTime now)
        {
            var sla = SlaCalculator.Evaluate(i, TargetFor(i.Priority, targets), now);

            return new IncidentSummaryResponse
            {
                Id = i.Id,
                IncidentNumber = i.IncidentNumber,
                Title = i.Title,
                Priority = i.Priority,
                Status = i.Status,
                CategoryName = i.Category?.Name,
                ReportedByUserId = i.ReportedByUserId,
                AssignedToUserId = i.AssignedToUserId,
                CreatedAtUtc = i.CreatedAtUtc,
                SlaState = sla.State,
                SlaDueAtUtc = sla.DueAtUtc
            };
        }

        private static IncidentDetailResponse ToDetail(
            Incident i,
            IReadOnlyDictionary<IncidentPriority, int> targets,
            DateTime now)
        {
            var sla = SlaCalculator.Evaluate(i, TargetFor(i.Priority, targets), now);

            return new IncidentDetailResponse
            {
                Id = i.Id,
                IncidentNumber = i.IncidentNumber,
                Title = i.Title,
                Description = i.Description,
                Priority = i.Priority,
                Status = i.Status,
                CategoryId = i.CategoryId,
                CategoryName = i.Category?.Name,
                ReportedByUserId = i.ReportedByUserId,
                AssignedToUserId = i.AssignedToUserId,
                CreatedAtUtc = i.CreatedAtUtc,
                UpdatedAtUtc = i.UpdatedAtUtc,
                ResolvedAtUtc = i.ResolvedAtUtc,
                SlaState = sla.State,
                SlaStartedAtUtc = sla.StartedAtUtc,
                SlaDueAtUtc = sla.DueAtUtc
            };
        }
    }
}
