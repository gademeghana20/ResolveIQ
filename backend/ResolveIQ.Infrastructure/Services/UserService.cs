using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Auth;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Application.DTOs.Users;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly TimeProvider _time;
        private readonly IPasswordService _passwords;
        private readonly IAuditLog _audit;
        private readonly INotifier _notifier;
        private readonly DemoSettings _demo;

        // Incidents still being worked on. A Resolved one is only waiting for its reporter, so it stays where it is.
        private static readonly IncidentStatus[] ActiveStatuses = { IncidentStatus.Open, IncidentStatus.InProgress, IncidentStatus.OnHold };

        public UserService(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            TimeProvider time,
            IPasswordService passwords,
            IAuditLog audit,
            INotifier notifier,
            Microsoft.Extensions.Options.IOptions<DemoSettings> demo)
        {
            _demo = demo.Value;
            _db = db;
            _userManager = userManager;
            _time = time;
            _passwords = passwords;
            _audit = audit;
            _notifier = notifier;
        }

        public async Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(
            CancellationToken cancellationToken = default) =>
            await LoadUsersAsync(onlyId: null, cancellationToken);

        public async Task<UserSummaryResponse?> GetUserAsync(string id, CancellationToken cancellationToken = default) =>
            (await LoadUsersAsync(id, cancellationToken)).FirstOrDefault();

        public async Task<UserChangeResult> CreateUserAsync(
            CreateUserRequest request,
            CancellationToken cancellationToken = default)
        {
            var role = CanonicalRole(request.Role);
            if (role is null)
            {
                return Invalid("Role", RoleMessage);
            }

            var user = new IdentityUser { UserName = request.Email, Email = request.Email };

            // The account starts with a long random password that nobody (not even the Admin) ever sees. The person
            // chooses their own through the emailed link, so until then the account cannot be signed in to.
            var created = await _userManager.CreateAsync(user, UnknowablePassword());
            if (!created.Succeeded)
            {
                return new UserChangeResult(
                    ChangeOutcome.Invalid,
                    Errors: created.Errors.Select(e => new FieldError(FieldFor(e.Code), e.Description)).ToList());
            }

            await _userManager.AddToRoleAsync(user, role);
            await _passwords.SendLinkAsync(user.Id, PasswordLinkKind.Invitation, cancellationToken);
            await _audit.RecordAsync(
                AuditActions.UserCreated, AuditActions.TargetUser, user.Id, user.Email, $"Role: {role}", cancellationToken: cancellationToken);

            return await SuccessAsync(user.Id, cancellationToken);
        }

        public async Task<UserChangeResult> ChangeRoleAsync(
            string id,
            string role,
            string actingUserId,
            CancellationToken cancellationToken = default)
        {
            // Nobody can demote themselves, so there is always at least one Admin left who can fix things.
            if (id == actingUserId)
            {
                return Invalid("Id", "You cannot change your own role.");
            }

            var newRole = CanonicalRole(role);
            if (newRole is null)
            {
                return Invalid("Role", RoleMessage);
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return new UserChangeResult(ChangeOutcome.NotFound);
            }

            if (_demo.Protects(user.Email))
            {
                return Invalid("Id", DemoSettings.ProtectedMessage);
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!(currentRoles.Count == 1 && currentRoles[0] == newRole))
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, newRole);

                // Changes the user's security stamp, so any token carrying the old role stops working now.
                await _userManager.UpdateSecurityStampAsync(user);

                await _audit.RecordAsync(
                    AuditActions.UserRoleChanged, AuditActions.TargetUser, user.Id, user.Email,
                    $"Role: {string.Join(", ", currentRoles)} → {newRole}", cancellationToken: cancellationToken);
            }

            return await SuccessAsync(user.Id, cancellationToken);
        }

        public async Task<UserChangeResult> SendPasswordResetEmailAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            var target = await _userManager.FindByIdAsync(id);
            if (target is null)
            {
                return new UserChangeResult(ChangeOutcome.NotFound);
            }

            if (_demo.Protects(target.Email))
            {
                return Invalid("Id", DemoSettings.ProtectedMessage);
            }

            // Sending changes nothing about the account; the person has to follow the link to choose a password.
            if (!await _passwords.SendLinkAsync(id, PasswordLinkKind.AdminRequested, cancellationToken))
            {
                return Invalid("Id", "This person is deactivated, so no email was sent. Reactivate them first.");
            }

            await _audit.RecordAsync(
                AuditActions.UserResetEmailSent, AuditActions.TargetUser, target.Id, target.Email, cancellationToken: cancellationToken);
            return await SuccessAsync(id, cancellationToken);
        }

        public async Task<UserChangeResult> SetActiveAsync(
            string id,
            bool isActive,
            string actingUserId,
            string? reassignToUserId = null,
            CancellationToken cancellationToken = default)
        {
            if (id == actingUserId)
            {
                return Invalid("Id", "You cannot deactivate or reactivate yourself.");
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return new UserChangeResult(ChangeOutcome.NotFound);
            }

            if (!isActive && _demo.Protects(user.Email))
            {
                return Invalid("Id", DemoSettings.ProtectedMessage);
            }

            if (isActive && !string.IsNullOrEmpty(reassignToUserId))
            {
                return Invalid("ReassignToUserId", "Incidents can only be reassigned when deactivating someone.");
            }

            var wasActive = !(user.LockoutEnd > _time.GetUtcNow());
            var reassigned = 0;
            string? reassignedTo = null;

            // Hand the unfinished incidents over first, so a failed hand-over leaves the person untouched.
            if (!isActive && !string.IsNullOrEmpty(reassignToUserId))
            {
                var handOver = await ReassignActiveIncidentsAsync(user, reassignToUserId, actingUserId, cancellationToken);
                if (handOver.Error is not null)
                {
                    return handOver.Error;
                }

                reassigned = handOver.Count;
                reassignedTo = handOver.NewAssigneeEmail;
            }

            if (isActive)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);
            }
            else
            {
                // Deactivation reuses Identity's lockout, locked until the far future.
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            }

            // Any token issued before this change stops working immediately.
            await _userManager.UpdateSecurityStampAsync(user);

            if (wasActive != isActive)
            {
                var details = reassigned > 0 ? $"Handed {reassigned} unfinished incident(s) to {reassignedTo}" : null;
                await _audit.RecordAsync(
                    isActive ? AuditActions.UserReactivated : AuditActions.UserDeactivated,
                    AuditActions.TargetUser, user.Id, user.Email, details, cancellationToken: cancellationToken);
            }

            return await SuccessAsync(user.Id, cancellationToken);
        }

        // ---- helpers ----

        private record HandOver(int Count, string? NewAssigneeEmail, UserChangeResult? Error);

        // Moves every unfinished incident of a leaving person to someone active, with a history line and a notification each.
        private async Task<HandOver> ReassignActiveIncidentsAsync(
            IdentityUser leaving,
            string newAssigneeId,
            string actingUserId,
            CancellationToken cancellationToken)
        {
            if (newAssigneeId == leaving.Id)
            {
                return new HandOver(0, null, Invalid("ReassignToUserId", "Choose someone other than the person being deactivated."));
            }

            var newAssignee = await _userManager.FindByIdAsync(newAssigneeId);
            if (newAssignee is null)
            {
                return new HandOver(0, null, Invalid("ReassignToUserId", "No user exists with that id."));
            }

            if (newAssignee.LockoutEnd > _time.GetUtcNow())
            {
                return new HandOver(0, null, Invalid("ReassignToUserId", "That user is inactive and cannot be assigned incidents."));
            }

            var incidents = await _db.Incidents
                .Where(i => i.AssignedToUserId == leaving.Id && ActiveStatuses.Contains(i.Status))
                .ToListAsync(cancellationToken);

            var now = _time.GetUtcNow().UtcDateTime;
            var actor = await _db.Users.AsNoTracking()
                .Where(u => u.Id == actingUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken) ?? "an administrator";

            foreach (var incident in incidents)
            {
                incident.AssignedToUserId = newAssignee.Id;
                incident.UpdatedAtUtc = now;

                _db.IncidentHistory.Add(new IncidentHistory
                {
                    IncidentId = incident.Id,
                    ChangedByUserId = actingUserId,
                    FieldChanged = "AssignedTo",
                    OldValue = leaving.Email,
                    NewValue = newAssignee.Email,
                    ChangedAtUtc = now
                });

                var label = NotificationText.Label(incident);
                var batch = new NotificationBatch();
                batch.Add(newAssignee.Id, "Assigned", $"{label} was assigned to you by {actor} (it belonged to {leaving.Email}, who was deactivated).");
                batch.Add(incident.ReportedByUserId, "Assigned", $"{label} was assigned to {newAssignee.Email}.");
                _notifier.Queue(incident.Id, batch, exceptUserId: actingUserId);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new HandOver(incidents.Count, newAssignee.Email, null);
        }

        // Long and random, with every kind of character Identity asks for. Used only as a placeholder.
        private static string UnknowablePassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(33)) + "aA1!";

        private const string RoleMessage = "The role must be Admin, Manager or Employee.";

        // Accepts any capitalisation ("manager") and returns the official spelling ("Manager"), or null.
        private static string? CanonicalRole(string role) =>
            RoleNames.All.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));

        private static string FieldFor(string identityErrorCode) => identityErrorCode switch
        {
            var c when c.StartsWith("Password", StringComparison.Ordinal) => "Password",
            "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName" => "Email",
            _ => string.Empty
        };

        private static UserChangeResult Invalid(string field, string message) =>
            new(ChangeOutcome.Invalid, Errors: new[] { new FieldError(field, message) });

        private async Task<UserChangeResult> SuccessAsync(string id, CancellationToken cancellationToken) =>
            new(ChangeOutcome.Success, await GetUserAsync(id, cancellationToken));

        private async Task<List<UserSummaryResponse>> LoadUsersAsync(string? onlyId, CancellationToken cancellationToken)
        {
            var users = _db.Users.AsNoTracking();
            if (onlyId is not null)
            {
                users = users.Where(u => u.Id == onlyId);
            }

            var rows = await users
                .OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.Email, u.LockoutEnd })
                .ToListAsync(cancellationToken);

            var activeCounts = await _db.Incidents.AsNoTracking()
                .Where(i => i.AssignedToUserId != null && ActiveStatuses.Contains(i.Status))
                .GroupBy(i => i.AssignedToUserId!)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

            var roleRows = await (
                    from ur in _db.UserRoles.AsNoTracking()
                    join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                    select new { ur.UserId, RoleName = r.Name })
                .ToListAsync(cancellationToken);

            var rolesByUser = roleRows
                .Where(x => x.RoleName != null)
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName!).ToList());

            var now = _time.GetUtcNow();

            return rows
                .Select(u => new UserSummaryResponse
                {
                    Id = u.Id,
                    Email = u.Email,
                    Roles = rolesByUser.TryGetValue(u.Id, out var roles) ? roles : new List<string>(),
                    IsActive = !(u.LockoutEnd > now),
                    ActiveAssignedCount = activeCounts.TryGetValue(u.Id, out var count) ? count : 0
                })
                .ToList();
        }
    }
}
