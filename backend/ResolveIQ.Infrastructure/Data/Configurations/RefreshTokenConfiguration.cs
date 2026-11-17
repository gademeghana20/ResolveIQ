using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.Property(t => t.UserId)
                .IsRequired()
                .HasMaxLength(450);

            // A SHA-256 hash written in hex is always 64 characters. Looked up by this, so it is unique and indexed.
            builder.Property(t => t.TokenHash)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(t => t.SecurityStamp)
                .IsRequired()
                .HasMaxLength(256);

            builder.HasIndex(t => t.TokenHash).IsUnique();

            // "Cancel everything this person holds" looks tickets up by user.
            builder.HasIndex(t => t.UserId);

            // Tickets belong to their person: if the user is ever removed, their tickets go too.
            builder.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
