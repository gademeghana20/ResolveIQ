using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class IncidentHistoryConfiguration : IEntityTypeConfiguration<IncidentHistory>
    {
        public void Configure(EntityTypeBuilder<IncidentHistory> builder)
        {
            // The database hands out 1, 2, 3... as rows are saved.
            builder.Property(h => h.Sequence)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn();

            builder.Property(h => h.ChangedByUserId)
                .IsRequired()
                .HasMaxLength(450);

            builder.Property(h => h.FieldChanged)
                .IsRequired()
                .HasMaxLength(100);

            // As long as the longest thing that is recorded: an incident's description (4000 characters).
            builder.Property(h => h.OldValue)
                .HasMaxLength(4000);

            builder.Property(h => h.NewValue)
                .HasMaxLength(4000);

            // History belongs to its incident, so deleting an incident deletes its history.
            builder.HasOne<Incident>()
                .WithMany()
                .HasForeignKey(h => h.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // The person who made a change must stay known, so users with history cannot be deleted.
            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(h => h.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // History is always read for one incident, oldest to newest.
            builder.HasIndex(h => new { h.IncidentId, h.ChangedAtUtc });
        }
    }
}
