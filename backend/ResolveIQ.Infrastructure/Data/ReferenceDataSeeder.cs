using Microsoft.EntityFrameworkCore;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data
{
    public static class ReferenceDataSeeder
    {
        private static readonly (string Name, string Description)[] Categories =
        {
            ("Hardware", "Computers, printers, peripherals and other physical equipment"),
            ("Software", "Application errors, installations and licensing"),
            ("Network", "Connectivity, VPN, Wi-Fi and internet access"),
            ("Access and Permissions", "Accounts, passwords and access requests"),
            ("Database", "Database availability, performance and data issues"),
            ("Other", "Anything that does not fit another category")
        };

        public static async Task SeedCategoriesAsync(ApplicationDbContext db)
        {
            var existing = await db.Categories.Select(c => c.Name).ToListAsync();

            foreach (var (name, description) in Categories)
            {
                if (!existing.Contains(name))
                {
                    db.Categories.Add(new Category { Name = name, Description = description });
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
