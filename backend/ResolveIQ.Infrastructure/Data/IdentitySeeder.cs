using Microsoft.AspNetCore.Identity;

namespace ResolveIQ.Infrastructure.Data
{
    public static class IdentitySeeder
    {
        public static readonly string[] Roles = { "Admin", "Manager", "Employee" };

        public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            foreach (var role in Roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }
    }
}
