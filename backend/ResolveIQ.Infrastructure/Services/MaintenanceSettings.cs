namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>The "Maintenance" section of the settings: the housekeeping that runs by itself in the background.</summary>
    public class MaintenanceSettings
    {
        public const string SectionName = "Maintenance";

        /// <summary>Whether the housekeeping runs at all. Tests turn it off and run each job directly.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>How often it runs.</summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>
        /// A resolved incident that its reporter has not confirmed or reopened is closed automatically after this many
        /// days. 0 switches this off.
        /// </summary>
        public int AutoCloseResolvedAfterDays { get; set; } = 7;
    }
}
