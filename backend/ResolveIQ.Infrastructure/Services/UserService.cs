using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Users;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _db;

        public UserService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(
            CancellationToken cancellationToken = default)
        {
            var users = await _db.Users.AsNoTracking()
                .OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.Email })
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

            return users
                .Select(u => new UserSummaryResponse
                {
                    Id = u.Id,
                    Email = u.Email,
                    Roles = rolesByUser.TryGetValue(u.Id, out var roles) ? roles : new List<string>()
                })
                .ToList();
        }
    }
}
