namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>General application settings (the "App" section of the configuration).</summary>
    public class AppSettings
    {
        public const string SectionName = "App";

        /// <summary>Where the web app lives. Emailed links (such as "reset your password") point here.</summary>
        public string FrontendUrl { get; set; } = "http://localhost:4200";
    }
}
