namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>
    /// Settings for a public demo site (the "Demo" section of the configuration). On a site that anyone can open, the
    /// shared demo accounts must not be locked, demoted or given a new password by a visitor, or nobody else could use them.
    /// </summary>
    public class DemoSettings
    {
        public const string SectionName = "Demo";

        /// <summary>When true, the shared demo accounts cannot be deactivated, demoted or have their password changed.</summary>
        public bool ProtectAccounts { get; set; }

        /// <summary>The shared demo accounts that the public demo sign-in buttons use.</summary>
        public static readonly string[] SharedAccounts =
        {
            "admin@example.com",
            "manager@example.com",
            "employee@example.com"
        };

        public const string ProtectedMessage = "This is a shared demo account, so this change is switched off to keep the demo working for everyone.";

        public bool Protects(string? email) =>
            ProtectAccounts && email is not null && SharedAccounts.Contains(email, StringComparer.OrdinalIgnoreCase);
    }
}
