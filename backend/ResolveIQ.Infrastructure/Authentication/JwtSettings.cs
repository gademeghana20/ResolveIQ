namespace ResolveIQ.Infrastructure.Authentication
{
    public class JwtSettings
    {
        public const string SectionName = "Jwt";

        public string Key { get; set; } = string.Empty;

        public string Issuer { get; set; } = string.Empty;

        public string Audience { get; set; } = string.Empty;

        /// <summary>How long a login token lasts. Short on purpose; the refresh token renews it quietly.</summary>
        public int ExpiryMinutes { get; set; } = 15;

        /// <summary>How long a refresh token lasts, so how long someone can stay signed in without typing a password.</summary>
        public int RefreshTokenDays { get; set; } = 7;
    }
}
