using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResolveIQ.Domain.Entities;

namespace ResolveIQ.Infrastructure.Data.Configurations
{
    public class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
    {
        public void Configure(EntityTypeBuilder<SlaPolicy> builder)
        {
            // Exactly one policy per priority.
            builder.HasIndex(p => p.Priority).IsUnique();

            builder.ToTable(t => t.HasCheckConstraint(
                "CK_SlaPolicies_ResolutionTargetHours",
                "[ResolutionTargetHours] BETWEEN 1 AND 720"));
        }
    }
}
