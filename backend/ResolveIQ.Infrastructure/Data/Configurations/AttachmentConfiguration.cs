using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
    {
        public void Configure(EntityTypeBuilder<Attachment> builder)
        {
            builder.Property(a => a.Sequence)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn();

            builder.Property(a => a.UploadedByUserId)
                .IsRequired()
                .HasMaxLength(450);

            builder.Property(a => a.FileName)
                .IsRequired()
                .HasMaxLength(255);

            builder.Property(a => a.ContentType)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(a => a.Content)
                .IsRequired()
                .HasColumnType("varbinary(max)");

            // Files belong to their incident. (The link to the comment must not also cascade: SQL Server refuses two
            // cascade paths from one incident to the same table.)
            builder.HasOne<Incident>()
                .WithMany()
                .HasForeignKey(a => a.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<IncidentComment>()
                .WithMany()
                .HasForeignKey(a => a.CommentId)
                .OnDelete(DeleteBehavior.NoAction);

            // The uploader must stay known, like a comment's author.
            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(a => a.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Files are always read for one incident, oldest to newest.
            builder.HasIndex(a => new { a.IncidentId, a.CreatedAtUtc });
            builder.HasIndex(a => a.CommentId);
        }
    }
}
