using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Infrastructure.Authentication;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        /// <summary>
        /// Two browser tabs can refresh at the same moment with the same ticket. The loser is simply told to retry
        /// rather than treated as a thief, as long as the ticket was used up only moments ago.
        /// </summary>
        private static readonly TimeSpan SimultaneousUseWindow = TimeSpan.FromSeconds(30);

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly JwtSettings _settings;
        private readonly TimeProvider _time;

        public RefreshTokenService(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IOptions<JwtSettings> settings,
            TimeProvider time)
        {
            _db = db;
            _userManager = userManager;
            _settings = settings.Value;
            _time = time;
        }

        public async Task<string> IssueAsync(string userId, string securityStamp, CancellationToken cancellationToken = default)
        {
            var (ticket, _) = await AddAsync(userId, securityStamp, cancellationToken);
            return ticket;
        }

        public async Task<RefreshResult> RotateAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            var failed = new RefreshResult(false);
            var now = _time.GetUtcNow().UtcDateTime;
            var hash = Hash(refreshToken);

            var stored = await _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
            if (stored is null)
            {
                return failed;
            }

            if (stored.RevokedAtUtc is { } revokedAt)
            {
                // A used-up ticket turning up again. Moments later is two tabs racing; much later means it was
                // copied, so everything this person holds is cancelled and they must sign in again.
                if (now - revokedAt > SimultaneousUseWindow)
                {
                    await RevokeAllAsync(stored.UserId, now, cancellationToken);
                }

                return failed;
            }

            if (stored.ExpiresAtUtc <= now)
            {
                return failed;
            }

            var user = await _userManager.FindByIdAsync(stored.UserId);
            if (user is null
                || await _userManager.IsLockedOutAsync(user)
                || !string.Equals(user.SecurityStamp, stored.SecurityStamp, StringComparison.Ordinal))
            {
                // Deactivated, locked out, or their password or role changed since this ticket was issued.
                await _db.RefreshTokens
                    .Where(t => t.Id == stored.Id && t.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
                return failed;
            }

            // Use the ticket up. This single statement only succeeds for one caller, so two requests that
            // arrive together cannot both walk away with a new ticket.
            var claimed = await _db.RefreshTokens
                .Where(t => t.Id == stored.Id && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
            if (claimed == 0)
            {
                return failed;
            }

            var (replacement, _) = await AddAsync(user.Id, user.SecurityStamp ?? string.Empty, cancellationToken);
            return new RefreshResult(true, user.Id, replacement);
        }

        public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var hash = Hash(refreshToken);

            await _db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
        }

        // ---- helpers ----

        private async Task<(string Ticket, RefreshToken Row)> AddAsync(
            string userId,
            string securityStamp,
            CancellationToken cancellationToken)
        {
            // 32 random bytes: far too many possibilities to guess. Written in a URL-safe form.
            var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            var now = _time.GetUtcNow().UtcDateTime;
            var row = new RefreshToken
            {
                UserId = userId,
                TokenHash = Hash(ticket),
                SecurityStamp = securityStamp,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(_settings.RefreshTokenDays)
            };

            _db.RefreshTokens.Add(row);
            await _db.SaveChangesAsync(cancellationToken);
            return (ticket, row);
        }

        private Task<int> RevokeAllAsync(string userId, DateTime now, CancellationToken cancellationToken) =>
            _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);

        private static string Hash(string ticket) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));
    }
}
