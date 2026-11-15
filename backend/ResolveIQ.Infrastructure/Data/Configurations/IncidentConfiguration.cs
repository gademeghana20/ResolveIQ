using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
    {
        public void Configure(EntityTypeBuilder<Incident> builder)
        {
            builder.Property(i => i.IncidentNumber)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(i => i.Title)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(i => i.Description)
                .IsRequired()
                .HasMaxLength(4000);

            builder.Property(i => i.ReportedByUserId)
                .IsRequired()
                .HasMaxLength(450);

            builder.Property(i => i.AssignedToUserId)
                .HasMaxLength(450);

            builder.HasOne(i => i.Category)
                .WithMany()
                .HasForeignKey(i => i.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(i => i.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(i => i.AssignedToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(i => i.IncidentNumber).IsUnique();
            builder.HasIndex(i => i.Status);
            builder.HasIndex(i => i.AssignedToUserId);
            builder.HasIndex(i => i.CreatedAtUtc);
        }
    }
}
