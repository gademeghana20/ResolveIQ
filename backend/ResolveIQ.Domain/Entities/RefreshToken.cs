namespace ResolveIQ.Domain.Entities
{
    /// <summary>
    /// A long-lived "ticket" that lets someone get a new short-lived login token without typing their password again.
    /// Only a fingerprint (hash) of the ticket is stored, never the ticket itself, so a copy of the database cannot be
    /// used to sign in. Each ticket works once: using it gives a new ticket and retires the old one.
    /// </summary>
    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string UserId { get; set; } = string.Empty;

        /// <summary>The SHA-256 fingerprint (64 hex characters) of the ticket.</summary>
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>
        /// The user's security stamp when the ticket was issued. If the user's stamp has changed since (new password,
        /// new role, deactivated), the ticket is dead.
        /// </summary>
        public string SecurityStamp { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>When the ticket was used up, replaced or cancelled. Null while it is still usable.</summary>
        public DateTime? RevokedAtUtc { get; set; }
    }
}
