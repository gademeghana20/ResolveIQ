using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public const string IncidentNumberSequence = "IncidentNumberSeq";

        public DbSet<Incident> Incidents { get; set; } = null!;

        public DbSet<Category> Categories { get; set; } = null!;

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            // Every DateTime in the model is treated as UTC when read from the database.
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // A database counter that hands out 1, 2, 3... safely, even for simultaneous requests.
            // Used to build incident numbers like INC-000001.
            builder.HasSequence<long>(IncidentNumberSequence);
        }
    }
}