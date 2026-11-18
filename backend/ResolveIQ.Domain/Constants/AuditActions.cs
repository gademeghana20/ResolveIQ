namespace ResolveIQ.Domain.Constants
{
    /// <summary>The things the audit trail records. Kept as text so the trail stays readable if the code changes.</summary>
    public static class AuditActions
    {
        public const string UserCreated = "user.created";
        public const string UserRegistered = "user.registered";
        public const string UserRoleChanged = "user.role_changed";
        public const string UserDeactivated = "user.deactivated";
        public const string UserReactivated = "user.reactivated";
        public const string UserResetEmailSent = "user.reset_email_sent";
        public const string CategoryCreated = "category.created";
        public const string CategoryUpdated = "category.updated";
        public const string SlaPolicyUpdated = "sla.updated";

        public static readonly IReadOnlyList<string> All = new[]
        {
            UserCreated, UserRegistered, UserRoleChanged, UserDeactivated, UserReactivated, UserResetEmailSent,
            CategoryCreated, CategoryUpdated, SlaPolicyUpdated
        };

        public const string TargetUser = "User";
        public const string TargetCategory = "Category";
        public const string TargetSlaPolicy = "SlaPolicy";
    }
}
