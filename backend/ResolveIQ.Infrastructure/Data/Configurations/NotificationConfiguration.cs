using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
    {
        public void Configure(EntityTypeBuilder<Notification> builder)
        {
            // The database hands out 1, 2, 3... as rows are saved.
            builder.Property(n => n.Sequence)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn();

            builder.Property(n => n.UserId)
                .IsRequired()
                .HasMaxLength(450);

            builder.Property(n => n.Kind)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(n => n.Message)
                .IsRequired()
                .HasMaxLength(500);

            // Notifications belong to the person they are for.
            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // They belong to their incident. (Incidents are never deleted, and SQL Server allows only one
            // cascading path from the user table, so this is Restrict rather than cascade.)
            builder.HasOne<Incident>()
                .WithMany()
                .HasForeignKey(n => n.IncidentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Always read for one person, newest first, often only the unread ones.
            builder.HasIndex(n => new { n.UserId, n.ReadAtUtc, n.CreatedAtUtc });
        }
    }
}
