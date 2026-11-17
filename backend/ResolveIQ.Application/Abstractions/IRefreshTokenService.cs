namespace ResolveIQ.Application.Abstractions
{
    /// <summary>The result of exchanging a refresh token: who it belonged to and the replacement ticket.</summary>
    public record RefreshResult(bool Succeeded, string? UserId = null, string? NewRefreshToken = null);

    /// <summary>
    /// Refresh tokens: long-lived, single-use tickets that get a new short-lived login token without a password.
    /// </summary>
    public interface IRefreshTokenService
    {
        /// <summary>Creates a ticket for a user (after they sign in) and returns it. It is shown once and never stored in the clear.</summary>
        Task<string> IssueAsync(string userId, string securityStamp, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exchanges a ticket for a new one. Fails if the ticket is unknown, expired, already used, or if the person was
        /// deactivated, locked out or had their password or role changed since it was issued. A ticket that is used a
        /// second time (a sign it was copied) cancels every ticket that person holds.
        /// </summary>
        Task<RefreshResult> RotateAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>Cancels one ticket (signing out). Unknown or already used tickets are ignored.</summary>
        Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
    }
}
