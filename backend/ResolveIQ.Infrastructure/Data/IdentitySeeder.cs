using Microsoft.AspNetCore.Identity;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Infrastructure.Data
{
    public static class IdentitySeeder
    {
        // Fake addresses on example.com (a domain reserved for examples). Development only.
        private static readonly (string Email, string Role)[] DemoUsers =
        {
            ("admin@example.com", RoleNames.Admin),
            ("manager@example.com", RoleNames.Manager),
            ("employee@example.com", RoleNames.Employee)
        };

        public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            foreach (var role in RoleNames.All)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }

        public static async Task SeedDemoUsersAsync(UserManager<IdentityUser> userManager, string password)
        {
            foreach (var (email, role) in DemoUsers)
            {
                if (await userManager.FindByEmailAsync(email) is not null)
                {
                    continue;
                }

                var user = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Could not seed demo user {email}: {errors}");
                }

                await userManager.AddToRoleAsync(user, role);
            }
        }
    }
}
