using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgJoinRequestConfiguration : IEntityTypeConfiguration<OrgJoinRequest>
{
    public void Configure(EntityTypeBuilder<OrgJoinRequest> b)
    {
        b.ToTable("org_join_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.Message).HasColumnName("message");
        b.Property(x => x.Role).HasColumnName("role");
        b.Property(x => x.DecidedBy).HasColumnName("decided_by");
        b.Property(x => x.DecidedAt).HasColumnName("decided_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.Status });
        b.HasIndex(x => x.UserId);

        // The one-pending-per-(org,user) guarantee is a PARTIAL unique index, which
        // EF's fluent HasIndex cannot model — it lives in migration 0030 and is not
        // mirrored here. Do not "fix" that by adding .IsUnique() to the composite
        // index above: it would forbid the decided-row history the flow depends on.

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}
