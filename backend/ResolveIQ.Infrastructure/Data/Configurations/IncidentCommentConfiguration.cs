using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class IncidentCommentConfiguration : IEntityTypeConfiguration<IncidentComment>
    {
        public void Configure(EntityTypeBuilder<IncidentComment> builder)
        {
            // The database hands out 1, 2, 3... as rows are saved.
            builder.Property(c => c.Sequence)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn();

            builder.Property(c => c.AuthorUserId)
                .IsRequired()
                .HasMaxLength(450);

            builder.Property(c => c.Text)
                .IsRequired()
                .HasMaxLength(2000);

            // Comments belong to their incident, so deleting an incident deletes its comments.
            builder.HasOne<Incident>()
                .WithMany()
                .HasForeignKey(c => c.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // The author must stay known, so users who have commented cannot be deleted.
            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(c => c.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Comments are always read for one incident, oldest to newest.
            builder.HasIndex(c => new { c.IncidentId, c.CreatedAtUtc });
        }
    }
}
