using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

        // History columns hold as many characters as a description can have (4000), so that editing a long
        // description keeps its full old and new wording. Shorter values are never cut.
        private const int HistoryValueMaxLength = 4000;

        // Putting an incident on hold takes a short reason; resolving it takes the fuller account of the fix.
        private const int MaxHoldReasonLength = 500;

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _time;
        private readonly INotifier _notifier;
        private readonly int _autoCloseDays;

        public IncidentService(
            ApplicationDbContext db,
            TimeProvider time,
            INotifier notifier,
            IOptions<MaintenanceSettings> maintenance)
        {
            _db = db;
            _time = time;
            _notifier = notifier;
            _autoCloseDays = maintenance.Value.AutoCloseResolvedAfterDays;
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

            var now = Now();

            // The SLA state is worked out in code, not stored, so SQL cannot filter on it.
            // When it is requested, load the matches so far, filter them here, then page them here.
            if (query.SlaState is SlaState wantedState)
            {
                var matches = (await sorted.Include(i => i.Category).ToListAsync(cancellationToken))
                    .Select(i => ToSummary(i, now))
                    .Where(s => s.SlaState == wantedState)
                    .ToList();

                var matchingPage = matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
                await AddEmailsAsync(matchingPage, cancellationToken);

                return new PagedResult<IncidentSummaryResponse>
                {
                    Items = matchingPage,
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

            var items = page.Select(i => ToSummary(i, now)).ToList();
            await AddEmailsAsync(items, cancellationToken);

            return new PagedResult<IncidentSummaryResponse>
            {
                Items = items,
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

            if (incident is null)
            {
                return null;
            }

            var detail = ToDetail(incident, Now());
            await AddEmailsAsync(detail, cancellationToken);
            return detail;
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
                .ThenBy(h => h.Sequence)
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
                ReportedUrgency = request.Urgency,
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

                var previousAssigneeId = incident.AssignedToUserId;
                incident.AssignedToUserId = assignee.Id;
                AddHistory(incident.Id, actingUserId, "AssignedTo", previousAssignee, assignee.Email, now);
                changed = true;

                await QueueAssignmentNotificationsAsync(incident, previousAssigneeId, assignee.Id, assignee.Email, actingUserId, cancellationToken);
            }

            // The first assignment starts the SLA clock. Later reassignments do not restart it.
            // The hours are taken from today's policy and then kept on the incident for good.
            if (incident.SlaStartedAtUtc is null)
            {
                incident.SlaStartedAtUtc = now;
                incident.SlaTargetHours = TargetFor(incident.Priority, targets);
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

            if (newStatus == IncidentStatus.OnHold && trimmedReason!.Length > MaxHoldReasonLength)
            {
                return Invalid("Reason", $"The reason is too long: the limit is {MaxHoldReasonLength} characters.");
            }

            // Whoever reads the incident next (usually the reporter, deciding whether to confirm) needs to know what was done.
            if (newStatus == IncidentStatus.Resolved && string.IsNullOrEmpty(trimmedReason))
            {
                return Invalid("Reason", "Say how it was fixed before marking an incident resolved.");
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
                incident.ResolutionNotes = trimmedReason;
            }
            else if (oldStatus == IncidentStatus.Resolved && newStatus == IncidentStatus.InProgress)
            {
                if (incident.ResolvedAtUtc is DateTime resolvedAt)
                {
                    incident.SlaPausedSeconds += SecondsBetween(resolvedAt, now);
                }

                incident.ResolvedAtUtc = null; // reopened
                incident.ResolutionNotes = null; // the history still has what was written
            }

            if (newStatus == IncidentStatus.Closed)
            {
                incident.ClosedAtUtc = now;
            }

            AddHistory(incident.Id, userId, "Status", oldStatus.ToString(), newStatus.ToString(), now);
            if (newStatus == IncidentStatus.OnHold)
            {
                AddHistory(incident.Id, userId, "OnHoldReason", null, trimmedReason, now);
            }

            if (newStatus == IncidentStatus.Resolved)
            {
                AddHistory(incident.Id, userId, "ResolutionNotes", null, trimmedReason, now);
            }

            await QueueStatusNotificationsAsync(incident, oldStatus, newStatus, trimmedReason, userId, cancellationToken);

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

            var batch = new NotificationBatch();
            batch.Add(
                incident.AssignedToUserId,
                "Priority",
                $"The priority of {NotificationText.Label(incident)} is now {newPriority}.");
            _notifier.Queue(incident.Id, batch, exceptUserId: userId);

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

            // The title and description are the reporter's own account of the problem, so only the reporter
            // may reword them. Managers and Admins can still comment, assign, prioritise and move the status.
            if (incident.ReportedByUserId != userId)
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

        private async Task<string?> EmailOfUserAsync(string? userId, CancellationToken cancellationToken) =>
            userId is null
                ? null
                : await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        // Who is told when an incident is assigned: the new assignee, the one it was taken from, and the reporter.
        private async Task QueueAssignmentNotificationsAsync(
            Incident incident,
            string? previousAssigneeId,
            string newAssigneeId,
            string? newAssigneeEmail,
            string actingUserId,
            CancellationToken cancellationToken)
        {
            var label = NotificationText.Label(incident);
            var actor = await EmailOfUserAsync(actingUserId, cancellationToken) ?? "a manager";
            var batch = new NotificationBatch();

            batch.Add(newAssigneeId, "Assigned", $"{label} was assigned to you by {actor}.");
            batch.Add(previousAssigneeId, "Reassigned", $"{label} was reassigned to {newAssigneeEmail ?? "someone else"}.");
            batch.Add(incident.ReportedByUserId, "Assigned", $"{label} was assigned to {newAssigneeEmail ?? "someone"}.");

            _notifier.Queue(incident.Id, batch, exceptUserId: actingUserId);
        }

        // Who is told when the status changes: the reporter and the assignee (never the person who did it).
        private async Task QueueStatusNotificationsAsync(
            Incident incident,
            IncidentStatus oldStatus,
            IncidentStatus newStatus,
            string? notes,
            string actingUserId,
            CancellationToken cancellationToken)
        {
            var label = NotificationText.Label(incident);
            var actor = await EmailOfUserAsync(actingUserId, cancellationToken) ?? "someone";
            var batch = new NotificationBatch();

            if (newStatus == IncidentStatus.Resolved)
            {
                // The reporter is the one who has to decide, so they are told how it was fixed and what to do next.
                batch.Add(
                    incident.ReportedByUserId,
                    "Resolved",
                    $"{label} was resolved by {actor}: {NotificationText.Shorten(notes ?? string.Empty, 200)} Please confirm it is fixed, or reopen it.");
                batch.Add(incident.AssignedToUserId, "Status", $"{label} was marked resolved by {actor}.");
            }
            else if (oldStatus == IncidentStatus.Resolved && newStatus == IncidentStatus.InProgress)
            {
                var message = $"{label} was reopened by {actor}: it is back in progress.";
                batch.Add(incident.AssignedToUserId, "Reopened", message);
                batch.Add(incident.ReportedByUserId, "Reopened", message);
            }
            else if (newStatus == IncidentStatus.Closed)
            {
                var message = $"{label} was closed by {actor}.";
                batch.Add(incident.AssignedToUserId, "Closed", message);
                batch.Add(incident.ReportedByUserId, "Closed", message);
            }
            else
            {
                var message = $"{label} is now {NotificationText.StatusName(newStatus)} (changed by {actor}).";
                batch.Add(incident.ReportedByUserId, "Status", message);
                batch.Add(incident.AssignedToUserId, "Status", message);
            }

            _notifier.Queue(incident.Id, batch, exceptUserId: actingUserId);
        }

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

            // Ties are broken by the incident number, which is unique and counts up in creation order.
            // That keeps the order stable (an incident never appears on two pages) and "newest first"
            // truly newest first, even for incidents created at the same instant.
            return descending
                ? sorted.ThenByDescending(i => i.IncidentNumber)
                : sorted.ThenBy(i => i.IncidentNumber);
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
            string? changedByUserId,
            IReadOnlyDictionary<IncidentPriority, int> targets,
            DateTime now)
        {
            var oldPriority = incident.Priority;
            var previousState = SlaCalculator.Evaluate(incident, now).State;

            incident.Priority = newPriority;
            AddHistory(incident.Id, changedByUserId, "Priority", oldPriority?.ToString(), newPriority.ToString(), now);

            if (incident.SlaStartedAtUtc is not null)
            {
                incident.SlaStartedAtUtc = now;
                incident.SlaTargetHours = TargetFor(newPriority, targets); // the new priority's hours, as of now
                incident.SlaPausedSeconds = 0;
                incident.SlaPausedAtUtc = incident.Status == IncidentStatus.OnHold ? now : null;
                AddHistory(incident.Id, changedByUserId, "SlaClock", previousState.ToString(), "Restarted", now);
            }
        }

        private void AddHistory(
            Guid incidentId,
            string? changedByUserId,
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

            var detail = ToDetail(incident, Now());
            await AddEmailsAsync(detail, cancellationToken);
            return detail;
        }

        // ---- people's emails ----
        // Incidents store user ids. Screens want a readable name, so one extra query fetches the
        // emails of just the people who appear in what is being returned.

        private async Task AddEmailsAsync(IReadOnlyCollection<IncidentSummaryResponse> items, CancellationToken cancellationToken)
        {
            var emails = await LoadEmailsAsync(
                items.SelectMany(i => new[] { i.ReportedByUserId, i.AssignedToUserId }), cancellationToken);

            foreach (var item in items)
            {
                item.ReportedByEmail = EmailOf(emails, item.ReportedByUserId);
                item.AssignedToEmail = EmailOf(emails, item.AssignedToUserId);
            }
        }

        private async Task AddEmailsAsync(IncidentDetailResponse detail, CancellationToken cancellationToken)
        {
            var emails = await LoadEmailsAsync(new[] { detail.ReportedByUserId, detail.AssignedToUserId }, cancellationToken);

            detail.ReportedByEmail = EmailOf(emails, detail.ReportedByUserId);
            detail.AssignedToEmail = EmailOf(emails, detail.AssignedToUserId);
        }

        private async Task<Dictionary<string, string?>> LoadEmailsAsync(
            IEnumerable<string?> userIds,
            CancellationToken cancellationToken)
        {
            var ids = userIds.OfType<string>().Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<string, string?>();
            }

            return await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);
        }

        private static string? EmailOf(IReadOnlyDictionary<string, string?> emails, string? userId) =>
            userId is not null && emails.TryGetValue(userId, out var email) ? email : null;

        private static IncidentChangeResult Invalid(string field, string message) =>
            new(ChangeOutcome.Invalid, Field: field, Message: message);

        // ---- SLA and mapping ----

        // Today's active resolution targets, by priority. They are only used when a clock starts or restarts,
        // to decide the hours an incident is promised. After that the incident keeps those hours itself.
        private async Task<Dictionary<IncidentPriority, int>> LoadTargetHoursAsync(CancellationToken cancellationToken) =>
            await _db.SlaPolicies.AsNoTracking()
                .Where(p => p.IsActive)
                .ToDictionaryAsync(p => p.Priority, p => p.ResolutionTargetHours, cancellationToken);

        private static int? TargetFor(IncidentPriority? priority, IReadOnlyDictionary<IncidentPriority, int> targets) =>
            priority is IncidentPriority p && targets.TryGetValue(p, out var hours) ? hours : null;

        private static IncidentSummaryResponse ToSummary(Incident i, DateTime now)
        {
            var sla = SlaCalculator.Evaluate(i, now);

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

        private IncidentDetailResponse ToDetail(Incident i, DateTime now)
        {
            var sla = SlaCalculator.Evaluate(i, now);

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
                ClosedAtUtc = i.ClosedAtUtc,
                ReportedUrgency = i.ReportedUrgency,
                ResolutionNotes = i.ResolutionNotes,
                AutoCloseAtUtc = i.Status == IncidentStatus.Resolved && i.ResolvedAtUtc is DateTime resolvedAt && _autoCloseDays > 0
                    ? resolvedAt.AddDays(_autoCloseDays)
                    : null,
                SlaState = sla.State,
                SlaStartedAtUtc = sla.StartedAtUtc,
                SlaDueAtUtc = sla.DueAtUtc
            };
        }
    }
}
