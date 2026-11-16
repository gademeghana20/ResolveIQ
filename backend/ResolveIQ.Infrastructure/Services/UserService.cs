using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Common;
using ResolveIQ.Application.DTOs.Incidents;
using ResolveIQ.Application.DTOs.Users;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly TimeProvider _time;

        public UserService(ApplicationDbContext db, UserManager<IdentityUser> userManager, TimeProvider time)
        {
            _db = db;
            _userManager = userManager;
            _time = time;
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

            var created = await _userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return new UserChangeResult(
                    ChangeOutcome.Invalid,
                    Errors: created.Errors.Select(e => new FieldError(FieldFor(e.Code), e.Description)).ToList());
            }

            await _userManager.AddToRoleAsync(user, role);

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

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!(currentRoles.Count == 1 && currentRoles[0] == newRole))
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, newRole);

                // Changes the user's security stamp, so any token carrying the old role stops working now.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            return await SuccessAsync(user.Id, cancellationToken);
        }

        public async Task<UserChangeResult> SetActiveAsync(
            string id,
            bool isActive,
            string actingUserId,
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

            return await SuccessAsync(user.Id, cancellationToken);
        }

        // ---- helpers ----

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
                    IsActive = !(u.LockoutEnd > now)
                })
                .ToList();
        }
    }
}
