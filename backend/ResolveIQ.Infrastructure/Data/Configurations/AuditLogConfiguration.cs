using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
    {
        public void Configure(EntityTypeBuilder<AuditLog> builder)
        {
            builder.Property(a => a.Sequence)
                .ValueGeneratedOnAdd()
                .UseIdentityColumn();

            // No link to the users table on purpose: the trail keeps the emails as they were,
            // and must outlive any account.
            builder.Property(a => a.ActorUserId).HasMaxLength(450);
            builder.Property(a => a.ActorEmail).HasMaxLength(256);

            builder.Property(a => a.Action).IsRequired().HasMaxLength(50);
            builder.Property(a => a.TargetType).IsRequired().HasMaxLength(50);
            builder.Property(a => a.TargetId).HasMaxLength(450);
            builder.Property(a => a.TargetLabel).HasMaxLength(256);
            builder.Property(a => a.Details).HasMaxLength(1000);

            // Always read newest first, often for one action.
            builder.HasIndex(a => a.CreatedAtUtc);
            builder.HasIndex(a => new { a.Action, a.CreatedAtUtc });
        }
    }
}
