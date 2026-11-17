using ResolveIQ.Application.DTOs.Auth;

namespace ResolveIQ.Application.Abstractions
{
    /// <summary>
    /// Everything about passwords except signing in: changing your own, and choosing a new one through an
    /// emailed link. Nobody, not even an Admin, ever types a password for someone else.
    /// </summary>
    public interface IPasswordService
    {
        /// <summary>
        /// Changes a signed-in person's password after checking the current one. A wrong current password
        /// counts as a failed attempt (so it cannot be guessed endlessly). All their other logins stop working.
        /// </summary>
        Task<PasswordResult> ChangePasswordAsync(
            string userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// "Forgot password": emails a reset link if the address belongs to an active account.
        /// Does nothing visible otherwise, so the answer never reveals which addresses have accounts.
        /// </summary>
        Task SendForgottenLinkAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>
        /// Emails a reset link to one person on an Admin's behalf (or as their invitation).
        /// Returns false if the person does not exist or is deactivated, so nothing was sent.
        /// </summary>
        Task<bool> SendLinkAsync(string userId, PasswordLinkKind kind, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a new password using the one-time token from the emailed link. The token works once and
        /// expires. The person's other logins stop working, and a short lock-out from failed attempts is lifted.
        /// </summary>
        Task<PasswordResult> ResetPasswordAsync(
            string email,
            string token,
            string newPassword,
            CancellationToken cancellationToken = default);
    }
}
