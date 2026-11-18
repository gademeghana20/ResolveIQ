namespace ResolveIQ.API.Registration
{
    /// <summary>The "Registration" section of the settings: who may create their own account.</summary>
    public class RegistrationSettings
    {
        public const string SectionName = "Registration";

        /// <summary>
        /// Whether people can create their own account. Switch it off and only an Admin can add people.
        /// Set it with the environment variable Registration__Enabled=false.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// If this list has anything in it, only email addresses ending in one of these domains may register
        /// (for example "example.com"). Empty means any domain. Environment variables:
        /// Registration__AllowedEmailDomains__0=example.com
        /// </summary>
        public string[] AllowedEmailDomains { get; set; } = [];
    }

    /// <summary>What the sign-up page needs to know before it shows a form.</summary>
    public class RegistrationStatusResponse
    {
        public bool Enabled { get; set; }

        public IReadOnlyList<string> AllowedEmailDomains { get; set; } = Array.Empty<string>();
    }
}
