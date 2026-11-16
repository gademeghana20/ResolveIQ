using Microsoft.EntityFrameworkCore;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;

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

        // The starting resolution targets. Admins can change them later through the API.
        private static readonly (IncidentPriority Priority, int Hours)[] SlaTargets =
        {
            (IncidentPriority.Critical, 4),
            (IncidentPriority.High, 8),
            (IncidentPriority.Medium, 24),
            (IncidentPriority.Low, 48)
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

        public static async Task SeedSlaPoliciesAsync(ApplicationDbContext db)
        {
            var existing = await db.SlaPolicies.Select(p => p.Priority).ToListAsync();

            foreach (var (priority, hours) in SlaTargets)
            {
                if (!existing.Contains(priority))
                {
                    db.SlaPolicies.Add(new SlaPolicy { Priority = priority, ResolutionTargetHours = hours });
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
