using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgBuyerRequestConfiguration : IEntityTypeConfiguration<OrgBuyerRequest>
{
    public void Configure(EntityTypeBuilder<OrgBuyerRequest> b)
    {
        b.ToTable("org_buyer_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.RequestedBy).HasColumnName("requested_by");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.EntityType).HasColumnName("entity_type");
        b.Property(x => x.Ministry).HasColumnName("ministry");
        b.Property(x => x.Department).HasColumnName("department");
        b.Property(x => x.Office).HasColumnName("office");
        b.Property(x => x.ProcuringEntityCode).HasColumnName("procuring_entity_code");
        b.Property(x => x.Justification).HasColumnName("justification");
        b.Property(x => x.DecisionNote).HasColumnName("decision_note");
        b.Property(x => x.DecidedAt).HasColumnName("decided_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.Status });
        b.HasIndex(x => x.OrgId);

        // The one-pending-per-org guarantee is a PARTIAL unique index that EF's fluent API cannot
        // model — it lives in migration 0033. Do not add .IsUnique() to the composite index above:
        // it would forbid the decided-row history the reapply flow depends on. (Same note as
        // OrgJoinRequestConfiguration.)

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequestedBy);
    }
}
