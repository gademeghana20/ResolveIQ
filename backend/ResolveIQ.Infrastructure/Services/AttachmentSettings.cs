namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>The "Attachments" section of the configuration.</summary>
    public class AttachmentSettings
    {
        public const string SectionName = "Attachments";

        /// <summary>No single file may ever be bigger than this, whatever the settings say.</summary>
        public const long HardMaxFileBytes = 10L * 1024 * 1024;

        /// <summary>
        /// Whether people may attach files. Left unset, it is ON, except on a public demo site (Demo:ProtectAccounts), where
        /// it is OFF: strangers must not be able to put files on a site that is not theirs. Set it to true or false to decide
        /// yourself. Files that were already attached can always be downloaded.
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>The biggest single file, in bytes. Default 5 MB.</summary>
        public long MaxFileBytes { get; set; } = 5L * 1024 * 1024;

        /// <summary>How many files one incident may hold in total (its own and its comments').</summary>
        public int MaxFilesPerIncident { get; set; } = 20;

        /// <summary>How many files one comment may carry.</summary>
        public int MaxFilesPerComment { get; set; } = 5;

        /// <summary>The size limit actually used: never above the hard ceiling, never below 1 KB.</summary>
        public long EffectiveMaxFileBytes => Math.Clamp(MaxFileBytes, 1024, HardMaxFileBytes);

        public bool IsEnabled(DemoSettings demo) => Enabled ?? !demo.ProtectAccounts;
    }
}
