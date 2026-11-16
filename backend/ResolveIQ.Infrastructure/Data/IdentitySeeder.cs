using Microsoft.AspNetCore.Identity;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Infrastructure.Data
{
    public enum BootstrapAdminResult
    {
        Created,
        AdminAlreadyExists,
        EmailAlreadyRegistered
    }

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

        /// <summary>
        /// Creates the very first Admin, but only if no Admin exists yet. Without this, a freshly deployed
        /// system would have nobody who can promote anyone. Once an Admin exists, it does nothing.
        /// </summary>
        public static async Task<BootstrapAdminResult> EnsureFirstAdminAsync(
            UserManager<IdentityUser> userManager,
            string email,
            string password)
        {
            if ((await userManager.GetUsersInRoleAsync(RoleNames.Admin)).Count > 0)
            {
                return BootstrapAdminResult.AdminAlreadyExists;
            }

            // If someone already registered with this email, leave them alone. We cannot know who set
            // their password, so promoting them to Admin would be unsafe.
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                return BootstrapAdminResult.EmailAlreadyRegistered;
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
                throw new InvalidOperationException($"Could not create the first Admin ({email}): {errors}");
            }

            await userManager.AddToRoleAsync(user, RoleNames.Admin);
            return BootstrapAdminResult.Created;
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
