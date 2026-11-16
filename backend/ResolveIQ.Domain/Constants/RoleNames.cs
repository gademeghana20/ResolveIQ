namespace ResolveIQ.Domain.Constants
{
    public static class RoleNames
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Employee = "Employee";

        // For [Authorize(Roles = ...)]: a comma means "any one of these roles".
        public const string AdminOrManager = Admin + "," + Manager;

        public static readonly string[] All = { Admin, Manager, Employee };
    }
}
